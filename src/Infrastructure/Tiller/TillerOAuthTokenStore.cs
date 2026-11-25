using System.Net.Http.Json;
using System.Text.Json;

namespace Infrastructure.Tiller;

// A dedicated public OAuth client granted by the owner. The token file is a
// writable, private host-mounted directory entry, NOT an Executor session token.
// One refresh at a time across scoped MCP clients; rotating refresh tokens
// must be durably replaced before a renewed access token is used.
public static class TillerOAuthTokenStore
{
	private static readonly SemaphoreSlim Gate = new(1, 1);
	private static readonly HttpClient TokenClient = new(new HttpClientHandler { AllowAutoRedirect = false })
	{
		Timeout = TimeSpan.FromSeconds(30),
	};

	public static Task<string> GetBearerAsync(Uri destination, string file, CancellationToken cancellationToken) =>
		GetBearerAsync(TokenClient, destination, file, cancellationToken);

	// Explicit HTTP client overload permits a synthetic transport regression.
	public static async Task<string> GetBearerAsync(HttpClient client, Uri destination, string file, CancellationToken cancellationToken)
	{
		if (destination.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(destination.UserInfo))
		{
			throw new InvalidOperationException("Invalid Tiller OAuth destination.");
		}

		await Gate.WaitAsync(cancellationToken);
		try
		{
			string path = Path.GetFullPath(file);
			using JsonDocument state = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
			JsonElement root = state.RootElement;
			string origin = destination.GetLeftPart(UriPartial.Authority);
			if (root.GetProperty("issuer").GetString() != origin + "/api/auth")
			{
				throw new InvalidOperationException("Tiller OAuth issuer mismatch.");
			}

			string clientId = Required(root, "client_id");
			string refresh = Required(root, "refresh_token");
			if (root.TryGetProperty("expires_at", out JsonElement expiry)
				&& expiry.TryGetInt64(out long expiresAt)
				&& expiresAt > DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds())
			{
				return Required(root, "access_token");
			}

			using FormUrlEncodedContent form = new(new Dictionary<string, string>
			{
				["grant_type"] = "refresh_token",
				["client_id"] = clientId,
				["refresh_token"] = refresh,
			});
			using HttpResponseMessage response = await client.PostAsync(
				origin + "/api/auth/oauth2/token", form, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				throw new InvalidOperationException("Tiller OAuth renewal refused; owner reconnection may be required.");
			}

			// A server may rotate the credential even if the caller cancels
			// after receipt. Finish persistence before allowing cancellation.
			JsonElement renewed = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: CancellationToken.None);
			string access = Required(renewed, "access_token");
			string nextRefresh = renewed.TryGetProperty("refresh_token", out _)
				? Required(renewed, "refresh_token") : refresh;
			if (!string.Equals(Required(renewed, "token_type"), "Bearer", StringComparison.OrdinalIgnoreCase)
				|| !renewed.TryGetProperty("expires_in", out JsonElement lifetime)
				|| !lifetime.TryGetInt32(out int seconds) || seconds <= 0)
			{
				throw new InvalidOperationException("Tiller OAuth renewal returned invalid token metadata.");
			}

			string replacement = JsonSerializer.Serialize(new
			{
				issuer = origin + "/api/auth",
				client_id = clientId,
				refresh_token = nextRefresh,
				access_token = access,
				expires_at = DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeSeconds(),
			});
			string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				FileStreamOptions options = new()
				{
					Mode = FileMode.CreateNew,
					Access = FileAccess.Write,
					Share = FileShare.None,
				};
				if (!OperatingSystem.IsWindows())
				{
					options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
				}
				await using (FileStream stream = new(temporary, options))
				{
					byte[] bytes = System.Text.Encoding.UTF8.GetBytes(replacement);
					await stream.WriteAsync(bytes, CancellationToken.None);
					stream.Flush(flushToDisk: true);
				}
				File.Move(temporary, path, overwrite: true);
			}
			finally
			{
				if (File.Exists(temporary))
				{
					File.Delete(temporary);
				}
			}

			return access;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or HttpRequestException)
		{
			// Never propagate credential-bearing response or file contents.
			throw new InvalidOperationException("Tiller OAuth credential unavailable; no upstream read was sent.");
		}
		finally
		{
			Gate.Release();
		}
	}

	private static string Required(JsonElement root, string property)
	{
		if (!root.TryGetProperty(property, out JsonElement value)
			|| value.ValueKind != JsonValueKind.String
			|| string.IsNullOrWhiteSpace(value.GetString()))
		{
			throw new InvalidOperationException("Tiller OAuth credential metadata is incomplete.");
		}

		return value.GetString()!;
	}
}
