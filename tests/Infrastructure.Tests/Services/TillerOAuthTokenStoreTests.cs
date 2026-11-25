using System.Net;
using System.Text.Json;
using FluentAssertions;
using Infrastructure.Tiller;

namespace Infrastructure.Tests.Services;

public class TillerOAuthTokenStoreTests
{
	private sealed class RefreshHandler : HttpMessageHandler
	{
		public int Calls { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Calls++;
			request.RequestUri!.AbsoluteUri.Should().Be("https://executor.test/api/auth/oauth2/token");
			string form = await request.Content!.ReadAsStringAsync(cancellationToken);
			form.Should().Contain("grant_type=refresh_token").And.Contain("client_id=tracer").And.Contain("refresh_token=old-refresh");
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("""{"access_token":"new-access","refresh_token":"new-refresh","token_type":"Bearer","expires_in":3600}""", System.Text.Encoding.UTF8, "application/json"),
			};
		}
	}

	[Fact]
	public async Task ExpiredCredential_RotatesOnceAcrossClientsAndPersists()
	{
		string file = Path.GetTempFileName();
		try
		{
			await File.WriteAllTextAsync(file, """{"issuer":"https://executor.test/api/auth","client_id":"tracer","refresh_token":"old-refresh","access_token":"old-access","expires_at":0}""");
			RefreshHandler handler = new();
			using HttpClient client = new(handler);
			Uri destination = new("https://executor.test/mcp");
			string[] tokens = await Task.WhenAll(
				TillerOAuthTokenStore.GetBearerAsync(client, destination, file, CancellationToken.None),
				TillerOAuthTokenStore.GetBearerAsync(client, destination, file, CancellationToken.None));
			tokens.Should().Equal("new-access", "new-access");
			handler.Calls.Should().Be(1);
			using JsonDocument stored = JsonDocument.Parse(await File.ReadAllTextAsync(file));
			stored.RootElement.GetProperty("refresh_token").GetString().Should().Be("new-refresh");
		}
		finally
		{
			File.Delete(file);
		}
	}

	[Fact]
	public async Task DifferentIssuer_RefusesBeforeAnyNetworkCall()
	{
		string file = Path.GetTempFileName();
		try
		{
			await File.WriteAllTextAsync(file, """{"issuer":"https://other.test/api/auth","client_id":"tracer","refresh_token":"old-refresh","access_token":"old-access","expires_at":0}""");
			RefreshHandler handler = new();
			using HttpClient client = new(handler);
			Func<Task> act = () => TillerOAuthTokenStore.GetBearerAsync(client, new Uri("https://executor.test/mcp"), file, CancellationToken.None);
			await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*issuer mismatch*");
			handler.Calls.Should().Be(0);
		}
		finally
		{
			File.Delete(file);
		}
	}
}
