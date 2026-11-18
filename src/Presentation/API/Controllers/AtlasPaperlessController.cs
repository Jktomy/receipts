using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Admin-only same-origin boundary to the private Atlas receipt bridge.
/// No webhook/operator credentials are ever exposed to browser JavaScript.
/// Excluded from upstream OpenAPI schema; opt-in fork-specific feature.
/// </summary>
[ApiVersion("1.0")]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/atlas/paperless")]
[Authorize(Policy = "RequireAdmin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AtlasPaperlessController(IConfiguration configuration) : ControllerBase
{
	private static readonly HttpClient Bridge = new(new HttpClientHandler
	{
		AllowAutoRedirect = false,
		UseCookies = false,
		UseProxy = false
	})
	{
		Timeout = TimeSpan.FromSeconds(30)
	};

	private (Uri Url, string Secret)? PrivateBridge()
	{
		string? configured = configuration["AtlasBridge:Url"];
		string? secretPath = configuration["AtlasBridge:OperatorKeyFile"];
		if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(secretPath))
		{
			return null;
		}
		if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri? url) ||
			url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0)
		{
			return null;
		}

		// Never turn this authenticated controller into a general SSRF proxy.
		// Loopback is valid for a same-host development bridge; atlas-bridge is
		// the exact private Compose DNS name. External URLs are not accepted.
		bool allowed = url.Scheme == Uri.UriSchemeHttp &&
					   (url.Host == "atlas-bridge" ||
						(url.IsLoopback && url.Port == 8675)) &&
				   url.Port == 8675;
		if (!allowed || !System.IO.File.Exists(secretPath))
		{
			return null;
		}
		string secret;
		try
		{
			secret = System.IO.File.ReadAllText(secretPath).Trim();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
		return secret.Length >= 16 ? (url, secret) : null;
	}

	[HttpGet("status")]
	public IActionResult Status()
	{
		Response.Headers.CacheControl = "no-store";
		string? paperlessUiUrl = configuration["AtlasBridge:PaperlessBrowserUrl"];
		if (!Uri.TryCreate(paperlessUiUrl, UriKind.Absolute, out Uri? browse) ||
			browse.Scheme != Uri.UriSchemeHttps || browse.UserInfo.Length > 0 ||
			browse.Query.Length > 0 || browse.Fragment.Length > 0)
		{
			paperlessUiUrl = null;
		}
		return Ok(new { configured = PrivateBridge() is not null, paperlessBrowserUrl = paperlessUiUrl?.TrimEnd('/') });
	}

	[HttpGet("drafts")]
	public Task<IActionResult> List(CancellationToken ct) => Forward(HttpMethod.Get, "drafts", null, ct);

	[HttpGet("drafts/{id:int}")]
	public Task<IActionResult> Get(int id, CancellationToken ct) =>
		id > 0 ? Forward(HttpMethod.Get, $"drafts/{id}", null, ct) :
		Task.FromResult<IActionResult>(BadRequest(new { error = "Invalid document ID" }));

	[HttpPut("drafts/{id:int}")]
	public Task<IActionResult> Correct(int id, [FromBody] System.Text.Json.JsonElement body, CancellationToken ct) =>
		id > 0 ? Forward(HttpMethod.Put, $"drafts/{id}", body.GetRawText(), ct) :
		Task.FromResult<IActionResult>(BadRequest(new { error = "Invalid document ID" }));

	[HttpPost("drafts/{id:int}/import")]
	public async Task<IActionResult> Import(int id, CancellationToken ct)
	{
		if (id <= 0) return BadRequest(new { error = "Invalid document ID" });
		string payload;
		using (var reader = new System.IO.StreamReader(Request.Body))
		{
			payload = await reader.ReadToEndAsync(ct);
		}
		// Forward the reviewer client revision when present; empty bodies keep legacy behavior.
		if (string.IsNullOrWhiteSpace(payload)) payload = "{}";
		return await Forward(HttpMethod.Post, $"drafts/{id}/import", payload, ct);
	}

	[HttpPost("drafts/{id:int}/match")]
	public Task<IActionResult> Match(int id, [FromBody] System.Text.Json.JsonElement body, CancellationToken ct) =>
		id > 0 ? Forward(HttpMethod.Post, $"drafts/{id}/match", body.GetRawText(), ct) :
		Task.FromResult<IActionResult>(BadRequest(new { error = "Invalid document ID" }));

	[HttpPost("drafts/{id:int}/confirm-match")]
	public Task<IActionResult> ConfirmMatch(int id, [FromBody] System.Text.Json.JsonElement body, CancellationToken ct) =>
		id > 0 ? Forward(HttpMethod.Post, $"drafts/{id}/confirm-match", body.GetRawText(), ct) :
		Task.FromResult<IActionResult>(BadRequest(new { error = "Invalid document ID" }));

	private async Task<IActionResult> Forward(HttpMethod method, string path, string? body, CancellationToken ct)
	{
		Response.Headers.CacheControl = "no-store";
		Response.Headers["Referrer-Policy"] = "no-referrer";
		try
		{
			var config = PrivateBridge();
			if (config is null)
			{
				return StatusCode(503, new { error = "Atlas Paperless bridge is not configured" });
			}

			// Only constant route prefixes and bounded integer IDs reach the bridge.
			var target = new Uri(config.Value.Url, "/" + path);
			if (target.Host != config.Value.Url.Host || target.Port != config.Value.Url.Port)
			{
				return StatusCode(503, new { error = "Private bridge route rejected" });
			}

			using var request = new HttpRequestMessage(method, target);
			request.Headers.Add("X-Atlas-Operator-Key", config.Value.Secret);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			if (body is not null)
			{
				if (body.Length > 100_000)
				{
					return BadRequest(new { error = "Draft exceeds size limit" });
				}
				request.Content = new StringContent(body, Encoding.UTF8, "application/json");
			}

			using HttpResponseMessage response = await Bridge.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
			if (!response.IsSuccessStatusCode &&
				(response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
			{
				return StatusCode(502, new { error = "Bridge redirects are not allowed" });
			}

			if (response.Content.Headers.ContentLength > 1_000_000)
			{
				return StatusCode(502, new { error = "Bridge response exceeded size limit" });
			}
			string json = await response.Content.ReadAsStringAsync(ct);
			if (json.Length > 1_000_000)
			{
				return StatusCode(502, new { error = "Bridge response exceeded size limit" });
			}
			return new ContentResult
			{
				StatusCode = (int)response.StatusCode,
				ContentType = "application/json",
				Content = json
			};
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or UnauthorizedAccessException or ArgumentException)
		{
			return StatusCode(503, new { error = "Private Atlas bridge unavailable" });
		}
	}
}