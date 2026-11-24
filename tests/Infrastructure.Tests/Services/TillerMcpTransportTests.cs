using System.Net;
using System.Text.Json;
using Application.Models.Tiller;
using FluentAssertions;
using Infrastructure.Services;
using Infrastructure.Tiller;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Infrastructure.Tests.Services;

// RED-first transport tests: the app must speak MCP streamable-HTTP
// (POST /mcp, JSON-RPC tools/call, Bearer token from a secret handle)
// instead of dotted-path plain JSON. Token value is synthetic and
// file-local; live acceptance still needs a real granted credential.
public class TillerMcpTransportTests
{
	private sealed class McpSpyHandler : HttpMessageHandler
	{
		public List<(HttpMethod Method, string? Path, string? Auth, string Body, Dictionary<string, string> Headers)> Calls { get; } = [];
		private readonly Queue<HttpResponseMessage> _responses = new();

		public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

		public void EnqueueJson(string json, HttpStatusCode status = HttpStatusCode.OK, string? sessionId = null)
		{
			HttpResponseMessage response = new(status)
			{
				Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
			};
			if (sessionId is not null)
			{
				response.Headers.Add("Mcp-Session-Id", sessionId);
			}
			_responses.Enqueue(response);
		}

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
			Dictionary<string, string> headers = request.Headers
				.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
			Calls.Add((request.Method, request.RequestUri?.AbsolutePath, request.Headers.Authorization?.ToString(), body, headers));
			if (body.Contains("\"method\":\"notifications/initialized\""))
			{
				return new HttpResponseMessage(HttpStatusCode.Accepted);
			}
			if (_responses.Count == 0)
			{
				return new HttpResponseMessage(HttpStatusCode.InternalServerError);
			}
			return _responses.Dequeue();
		}
	}

	private static string WriteTokenFile(string token)
	{
		string path = Path.Combine(Path.GetTempPath(), $"tiller-token-{Guid.NewGuid():N}.txt");
		File.WriteAllText(path, token);
		return path;
	}

	private static TillerApiClient CreateMcpClient(
		McpSpyHandler spy,
		string? baseUrl = "https://executor-gateway.local/mcp/",
		string? accountId = "acc_test_tiller",
		string? tokenFile = null,
		bool withTokenFile = true)
	{
		tokenFile ??= withTokenFile ? WriteTokenFile("synthetic-test-bearer-token") : null;
		HttpClient httpClient = new(spy);
		if (baseUrl is not null)
		{
			httpClient.BaseAddress = new Uri(baseUrl);
		}

		Dictionary<string, string?> configValues = new();
		if (baseUrl is not null)
		{
			configValues["TILLER_BASE_URL"] = baseUrl;
		}
		if (accountId is not null)
		{
			configValues["TILLER_ACCOUNT_ID"] = accountId;
		}
		if (tokenFile is not null)
		{
			configValues["TILLER_TOKEN_FILE"] = tokenFile;
		}

		IConfiguration configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(configValues)
			.Build();

		return new TillerApiClient(
			httpClient,
			new MemoryCache(new MemoryCacheOptions()),
			configuration,
			new TillerClientOptions(),
			Mock.Of<ILogger<TillerApiClient>>());
	}

	private static string McpResult(string innerJson) =>
		"{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"content\":[{\"type\":\"text\",\"text\":"
		+ JsonSerializer.Serialize(innerJson) + "}]}}";

	[Fact]
	public async Task ListSpreadsheets_UsesMcpToolsCallEnvelope()
	{
		// Arrange: MCP initialize handshake then a tools/call result.
		const string inner = "{\"spreadsheets\":[{\"spreadsheet_id\":\"sheet-1\",\"spreadsheetTitle\":\"Household\",\"backendAutoFill\":true,\"editable\":true,\"accountCount\":24}],\"count\":1}";
		McpSpyHandler spy = new();
		spy.EnqueueJson("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-06-18\",\"serverInfo\":{\"name\":\"executor\",\"version\":\"x\"}}}", sessionId: "sess-1");
		spy.EnqueueJson(McpResult(inner));
		TillerApiClient client = CreateMcpClient(spy);

		// Act
		IReadOnlyList<TillerSpreadsheet> sheets = await client.ListSpreadsheetsAsync(CancellationToken.None);

		// Assert: parsed through the unwrapped envelope.
		sheets.Should().ContainSingle().Which.Should().Be(
			new TillerSpreadsheet("sheet-1", "Household", true, true, 24));
		spy.Calls.Should().HaveCount(3, "initialize + notifications/initialized + tools/call");
		spy.Calls[0].Path.Should().Be("/mcp");
		spy.Calls[0].Auth.Should().Be("Bearer synthetic-test-bearer-token", "the handshake itself is authenticated");
		spy.Calls[1].Auth.Should().Be("Bearer synthetic-test-bearer-token", "the initialized notification is authenticated");
		JsonDocument init = JsonDocument.Parse(spy.Calls[0].Body);
		init.RootElement.GetProperty("method").GetString().Should().Be("initialize");
		spy.Calls[2].Path.Should().Be("/mcp");
		spy.Calls[2].Auth.Should().Be("Bearer synthetic-test-bearer-token");
		JsonDocument call = JsonDocument.Parse(spy.Calls[2].Body);
		call.RootElement.GetProperty("method").GetString().Should().Be("tools/call");
		call.RootElement.GetProperty("params").GetProperty("name").GetString().Should().Be("tools.tiller.list_spreadsheets");
		call.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("accountId").GetString().Should().Be("acc_test_tiller");
		spy.Calls[2].Headers.Should().ContainKey("MCP-Protocol-Version")
			.WhoseValue.Should().Be("2025-06-18", "the negotiated version rides subsequent requests");
	}

	[Fact]
	public async Task McpErrorResult_FailsClosed()
	{
		// Arrange: server returns isError.
		McpSpyHandler spy = new();
		spy.EnqueueJson("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-06-18\"}}");
		spy.EnqueueJson("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"denied\"}],\"isError\":true}}");
		TillerApiClient client = CreateMcpClient(spy);

		// Act
		Func<Task> call = () => client.ListSpreadsheetsAsync(CancellationToken.None);

		// Assert
		await call.Should().ThrowAsync<InvalidOperationException>();
	}

	[Fact]
	public async Task Unauthorized_FailsClosedWithoutTokenLeak()
	{
		// Arrange: handshake OK, tools/call 401.
		McpSpyHandler spy = new();
		spy.EnqueueJson("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-06-18\"}}");
		spy.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized));
		TillerApiClient client = CreateMcpClient(spy);

		// Assert: fails closed and the bearer value never appears in the message.
		Func<Task> failing = () => client.ListSpreadsheetsAsync(CancellationToken.None);
		(await failing.Should().ThrowAsync<InvalidOperationException>().WithMessage("*401*")).Which.Message.Should().NotContain("synthetic-test-bearer-token");
	}

	[Fact]
	public async Task MissingTokenFile_FailsClosedBeforeTransport()
	{
		// Arrange: configured base + account but no token handle.
		McpSpyHandler spy = new();
		TillerApiClient client = CreateMcpClient(spy, withTokenFile: false);

		// Act
		Func<Task> call = () => client.ListSpreadsheetsAsync(CancellationToken.None);

		// Assert
		client.IsConfigured.Should().BeFalse("no app-authorized credential handle is wired");
		await call.Should().ThrowAsync<InvalidOperationException>();
		spy.Calls.Should().BeEmpty("nothing may leave the module without a credential handle");
	}

	[Fact]
	public async Task NonAllowlistedToolPath_NeverReachesTransport()
	{
		// Arrange: reflection drives the private choke point with a forged path.
		McpSpyHandler spy = new();
		TillerApiClient client = CreateMcpClient(spy);
		System.Reflection.MethodInfo invoke = typeof(TillerApiClient).GetMethod(
			"InvokeAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

		// Act
		Func<Task> call = () => (Task<JsonElement>)invoke.Invoke(
			client, ["tools.tiller.refresh_accounts", new { }, CancellationToken.None])!;

		// Assert
		await call.Should().ThrowAsync<InvalidOperationException>();
		spy.Calls.Should().BeEmpty("refresh/update paths must never leave the module");
	}

	[Fact]
	public async Task SseNotificationBeforeResult_SelectsMatchingResponseId()
	{
		// Arrange: stream emits a notification, then the result split across two data lines.
		const string inner = "{\"spreadsheets\":[{\"spreadsheet_id\":\"sheet-9\",\"spreadsheetTitle\":\"H\",\"backendAutoFill\":false,\"editable\":false,\"accountCount\":1}],\"count\":1}";
		string sseBody =
			"event: message\n" +
			"data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"progressToken\":7}}\n" +
			"\n" +
			"event: message\n" +
			"data: {\"jsonrpc\":\"2.0\",\"id\":2,\n" +
			"data: \"result\":{\"content\":[{\"type\":\"text\",\"text\":" + System.Text.Json.JsonSerializer.Serialize(inner) + "}]}}\n";
		McpSpyHandler spy = new();
		spy.EnqueueJson("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-06-18\"}}");
		HttpResponseMessage sse = new(HttpStatusCode.OK)
		{
			Content = new StringContent(sseBody, System.Text.Encoding.UTF8, "text/event-stream"),
		};
		spy.Enqueue(sse);
		TillerApiClient client = CreateMcpClient(spy);

		// Act
		IReadOnlyList<TillerSpreadsheet> sheets = await client.ListSpreadsheetsAsync(CancellationToken.None);

		// Assert: the matching-id result wins over the earlier notification.
		sheets.Should().ContainSingle().Which.Should().Be(
			new TillerSpreadsheet("sheet-9", "H", false, false, 1));
	}

	[Fact]
	public async Task SessionExpired404_ReinitializesOnceAndReplaysRead()
	{
		// Arrange: first tools/call 404s on a live session; re-handshake then succeeds.
		const string inner = "{\"spreadsheets\":[],\"count\":0}";
		McpSpyHandler spy = new();
		spy.EnqueueJson("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-06-18\"}}", sessionId: "sess-old");
		spy.Enqueue(new HttpResponseMessage(HttpStatusCode.NotFound));
		spy.EnqueueJson("{\"jsonrpc\":\"2.0\",\"id\":4,\"result\":{\"protocolVersion\":\"2025-06-18\"}}", sessionId: "sess-new");
		spy.EnqueueJson(McpResult(inner));
		TillerApiClient client = CreateMcpClient(spy);

		// Act
		IReadOnlyList<TillerSpreadsheet> sheets = await client.ListSpreadsheetsAsync(CancellationToken.None);

		// Assert: exactly one re-initialization and one read replay, then success.
		sheets.Should().BeEmpty();
		spy.Calls.Should().HaveCount(6, "init, initialized, call-404, re-init, re-initialized, replayed call");
		JsonDocument replay = JsonDocument.Parse(spy.Calls[5].Body);
		replay.RootElement.GetProperty("method").GetString().Should().Be("tools/call");
		replay.RootElement.GetProperty("params").GetProperty("name").GetString().Should().Be("tools.tiller.list_spreadsheets");
	}

	[Fact]
	public async Task Unauthorized401_NeverRetriesOrReinitializes()
	{
		// Arrange: handshake OK, tools/call 401.
		McpSpyHandler spy = new();
		spy.EnqueueJson("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-06-18\"}}");
		spy.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized));
		TillerApiClient client = CreateMcpClient(spy);

		// Act
		Func<Task> call = () => client.ListSpreadsheetsAsync(CancellationToken.None);

		// Assert: fail closed on the single attempt — no retry, no re-handshake.
		await call.Should().ThrowAsync<InvalidOperationException>();
		spy.Calls.Should().HaveCount(3, "initialize, initialized, one tools/call; 401 is terminal");
	}

	[Fact]
	public async Task NonHttpsBaseUrl_RejectedBeforeAnyTransport()
	{
		// Arrange: plain-HTTP base must never carry the bearer.
		McpSpyHandler spy = new();
		TillerApiClient client = CreateMcpClient(spy, baseUrl: "http://executor-gateway.local/mcp");

		// Act
		Func<Task> call = () => client.ListSpreadsheetsAsync(CancellationToken.None);

		// Assert: rejected before the credential handle is read or attached.
		(await call.Should().ThrowAsync<InvalidOperationException>().WithMessage("*https*")).And.Message.Should().Contain("TILLER_BASE_URL");
		spy.Calls.Should().BeEmpty("no request, and no bearer, may leave over HTTP");
	}
}
