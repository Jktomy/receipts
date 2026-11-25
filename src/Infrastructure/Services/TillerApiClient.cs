using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Interfaces.Services;
using Application.Models.Tiller;
using Infrastructure.Tiller;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

// Native read-only Tiller transport over MCP streamable-HTTP against the
// admitted Executor gateway (POST {base}/mcp, JSON-RPC "tools/call",
// Bearer token from an app-authorized secret handle). Verified live
// 2026-11-24: ONLY MCP /mcp exists (OAuth); dotted-path plain POST 404s.
// The argument shape {accountId, input} is unchanged from the verified
// live envelope; only the HTTP wrapper moved to MCP.
//
// Every outbound request targets one of the five allowlisted tool paths and
// carries the exact configured accountId. There is no code path in this file
// that constructs any other tool path — updates, deletes, category edits,
// holdings, balance history, and refresh can never leave this module.
public class TillerApiClient(
	HttpClient httpClient,
	IMemoryCache memoryCache,
	IConfiguration configuration,
	TillerClientOptions options,
	ILogger<TillerApiClient> logger) : ITillerApiClient
{
	public static readonly IReadOnlyList<string> ReadPaths =
	[
		"tools.tiller.list_spreadsheets",
		"tools.tiller.list_accounts",
		"tools.tiller.list_categories",
		"tools.tiller.describe_sheet",
		"tools.tiller.query_transactions",
	];

	private static readonly HashSet<string> DescribableSheets = new(StringComparer.OrdinalIgnoreCase)
	{
		"Transactions",
		"Categories",
	};

	private const string CacheKeyPrefix = "tiller:";

	private string? BaseUrl => configuration[Tiller.TillerConfigurationKeys.BaseUrl];
	private string? AccountId => configuration[Tiller.TillerConfigurationKeys.AccountId];
	private string? TokenFile => configuration["TILLER_TOKEN_FILE"];
	private string? OAuthFile => configuration["TILLER_OAUTH_FILE"];

	public bool IsConfigured =>
		!string.IsNullOrWhiteSpace(BaseUrl)
		&& !string.IsNullOrWhiteSpace(AccountId)
		&& (!string.IsNullOrWhiteSpace(OAuthFile) || !string.IsNullOrWhiteSpace(TokenFile));

	private void RequireConfigured()
	{
		if (!IsConfigured)
		{
			throw new InvalidOperationException(
				"Tiller read interface is not configured (TILLER_BASE_URL / TILLER_ACCOUNT_ID / TILLER_OAUTH_FILE or TILLER_TOKEN_FILE). No request was sent.");
		}
	}

	private static void RequireSpreadsheet(string spreadsheetId)
	{
		if (string.IsNullOrWhiteSpace(spreadsheetId))
		{
			throw new ArgumentException("spreadsheetId is required.", nameof(spreadsheetId));
		}
	}

	// Single choke point: the ONLY place an outbound request is built.
	// Tool path must be allowlisted; the account envelope is always bound.
	// Wire: MCP streamable-HTTP tools/call at {base}/mcp with a Bearer
	// token read from the app-authorized secret handle (TILLER_TOKEN_FILE).
	private readonly SemaphoreSlim _sessionLock = new(1, 1);
	private string? _sessionId;
	private string? _negotiatedVersion;
	private long _nextRequestId;

	private Uri McpUri
	{
		get
		{
			string baseUrl = (BaseUrl ?? string.Empty).TrimEnd('/');
			string endpoint = baseUrl.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase) ? baseUrl : baseUrl + "/mcp";
			if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
				|| !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException(
					"Tiller MCP destination must be an absolute https URI (TILLER_BASE_URL). No request was sent.");
			}

			return uri;
		}
	}

	private string LoadBearerToken()
	{
		string? file = TokenFile;
		if (string.IsNullOrWhiteSpace(file))
		{
			throw new InvalidOperationException(
				"Tiller bearer credential is not wired (TILLER_TOKEN_FILE). No request was sent.");
		}

		string token;
		try
		{
			token = File.ReadAllText(file).Trim();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			throw new InvalidOperationException(
				"Tiller bearer credential handle is unreadable. No request was sent.");
		}

		if (string.IsNullOrWhiteSpace(token))
		{
			throw new InvalidOperationException(
				"Tiller bearer credential is empty. No request was sent.");
		}

		return token;
	}

	private async Task EnsureMcpSessionAsync(string bearer, CancellationToken cancellationToken)
	{
		if (_sessionId is not null)
		{
			return;
		}

		await _sessionLock.WaitAsync(cancellationToken);
		try
		{
			if (_sessionId is not null)
			{
				return;
			}

			using HttpRequestMessage init = new(HttpMethod.Post, McpUri);
			init.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
			long initId = Interlocked.Increment(ref _nextRequestId);
			init.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			init.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
			init.Content = JsonContent.Create(new
			{
				jsonrpc = "2.0",
				id = initId,
				method = "initialize",
				@params = new
				{
					protocolVersion = "2025-06-18",
					capabilities = new { },
					clientInfo = new { name = "receipts-tiller", version = "1.0" },
				},
			});
			using HttpResponseMessage initResponse = await httpClient.SendAsync(init, cancellationToken);
			if (initResponse.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
			{
				throw new InvalidOperationException(
					$"Executor MCP authorization refused (HTTP {(int)initResponse.StatusCode}). No request was sent.");
			}
			initResponse.EnsureSuccessStatusCode();

			JsonElement initRoot = await ReadRpcBodyAsync(initResponse, initId, cancellationToken);
			if (initRoot.TryGetProperty("error", out _))
			{
				throw new InvalidOperationException("Executor MCP initialize failed. No request was sent.");
			}
			string? negotiated = initRoot.TryGetProperty("result", out JsonElement result)
				&& result.TryGetProperty("protocolVersion", out JsonElement version)
				? version.GetString()
				: null;
			if (!string.Equals(negotiated, "2025-06-18", StringComparison.Ordinal))
			{
				throw new InvalidOperationException(
					"Executor MCP protocol version is not 2025-06-18 (handshake unproven). No request was sent.");
			}

			_negotiatedVersion = negotiated;

			if (initResponse.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? sessionValues))
			{
				_sessionId = sessionValues.FirstOrDefault();
			}

			using HttpRequestMessage initialized = new(HttpMethod.Post, McpUri);
			initialized.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
			if (_sessionId is not null)
			{
				initialized.Headers.Add("Mcp-Session-Id", _sessionId);
			}
			if (_negotiatedVersion is not null)
			{
				initialized.Headers.Add("MCP-Protocol-Version", _negotiatedVersion);
			}
			initialized.Content = JsonContent.Create(new
			{
				jsonrpc = "2.0",
				method = "notifications/initialized",
			});
			using HttpResponseMessage _initialized = await httpClient.SendAsync(initialized, cancellationToken);
		}
		finally
		{
			_sessionLock.Release();
		}
	}

	private static async Task<JsonElement> ReadRpcBodyAsync(HttpResponseMessage response, long expectedId, CancellationToken cancellationToken)
	{
		string body = await response.Content.ReadAsStringAsync(cancellationToken);
		string? mediaType = response.Content.Headers.ContentType?.MediaType;
		if (mediaType is null || !mediaType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
		{
			using JsonDocument document = JsonDocument.Parse(body);
			return document.RootElement.Clone();
		}

		// SSE: split on blank lines into events, join each event's data:
		// lines, and return the event whose JSON-RPC id matches. A
		// notification emitted before the response never wins.
		List<string> dataLines = [];
		foreach (string rawLine in body.Split('\n'))
		{
			string line = rawLine.Trim();
			if (line.Length == 0)
			{
				JsonElement? matched = MatchEvent(dataLines, expectedId);
				if (matched.HasValue)
				{
					return matched.Value;
				}

				dataLines.Clear();
				continue;
			}

			if (line.StartsWith("data: ", StringComparison.Ordinal))
			{
				dataLines.Add(line["data: ".Length..]);
			}
		}

		JsonElement? last = MatchEvent(dataLines, expectedId);
		if (last.HasValue)
		{
			return last.Value;
		}

		throw new InvalidOperationException("Executor MCP response carried no message with the expected id. No result was accepted.");
	}

	private static JsonElement? MatchEvent(List<string> dataLines, long expectedId)
	{
		if (dataLines.Count == 0)
		{
			return null;
		}

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(string.Join("\n", dataLines));
		}
		catch (JsonException)
		{
			return null;
		}

		using (document)
		{
			if (document.RootElement.TryGetProperty("id", out JsonElement id)
				&& id.ValueKind == JsonValueKind.Number
				&& id.TryGetInt64(out long actual)
				&& actual == expectedId)
			{
				return document.RootElement.Clone();
			}
		}

		return null;
	}

	// Sends one tools/call and parses the id-matched response. On 404
	// against a live session the session is cleared, the handshake runs
	// once, and the single read is replayed exactly once. 401/403 never
	// retry; a second failure throws. No loops.
	private async Task<JsonElement> SendToolsCallAsync(string path, object input, string bearer, bool hadSession, CancellationToken cancellationToken)
	{
		(long callId, HttpRequestMessage call) = BuildToolsCall(path, input, bearer);
		using (call)
		{
			using HttpResponseMessage response = await httpClient.SendAsync(call, cancellationToken);
			if (response.StatusCode != System.Net.HttpStatusCode.NotFound || !hadSession)
			{
				return await ParseToolsCallResultAsync(response, callId, path, cancellationToken);
			}
		}

		_sessionId = null;
		_negotiatedVersion = null;
		await EnsureMcpSessionAsync(bearer, cancellationToken);

		logger.LogDebug("Tiller read {Path} retrying once after session reset", path);
		(long replayId, HttpRequestMessage replay) = BuildToolsCall(path, input, bearer);
		using (replay)
		{
			using HttpResponseMessage replayed = await httpClient.SendAsync(replay, cancellationToken);
			return await ParseToolsCallResultAsync(replayed, replayId, path, cancellationToken);
		}
	}

	private (long, HttpRequestMessage) BuildToolsCall(string path, object input, string bearer)
	{
		long callId = Interlocked.Increment(ref _nextRequestId);
		HttpRequestMessage call = new(HttpMethod.Post, McpUri);
		call.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
		call.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		call.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
		if (_sessionId is not null)
		{
			call.Headers.Add("Mcp-Session-Id", _sessionId);
		}

		if (_negotiatedVersion is not null)
		{
			call.Headers.Add("MCP-Protocol-Version", _negotiatedVersion);
		}

		call.Content = JsonContent.Create(new
		{
			jsonrpc = "2.0",
			id = callId,
			method = "tools/call",
			@params = new
			{
				name = path,
				arguments = new Dictionary<string, object?> { ["accountId"] = AccountId, ["input"] = input },
			},
		});
		return (callId, call);
	}

	private static async Task<JsonElement> ParseToolsCallResultAsync(HttpResponseMessage response, long callId, string path, CancellationToken cancellationToken)
	{
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException(
				$"Tiller MCP call {path} failed (HTTP {(int)response.StatusCode}). No result was accepted.");
		}

		return await ReadRpcBodyAsync(response, callId, cancellationToken);
	}

	private async Task<JsonElement> InvokeAsync(string path, object input, CancellationToken cancellationToken)
	{
		RequireConfigured();
		if (!ReadPaths.Contains(path))
		{
			throw new InvalidOperationException($"Tiller tool path '{path}' is not in the read-only allowlist. No request was sent.");
		}

		// Strict order: destination validated (https) before the credential
		// handle is even read; the bearer is never attached to a rejected route.
		Uri destination = McpUri;
		string bearer = !string.IsNullOrWhiteSpace(OAuthFile)
			? await TillerOAuthTokenStore.GetBearerAsync(destination, OAuthFile, cancellationToken)
			: LoadBearerToken();
		await EnsureMcpSessionAsync(bearer, cancellationToken);

		logger.LogDebug("Tiller read {Path}", path);
		JsonElement root = await SendToolsCallAsync(path, input, bearer, hadSession: _sessionId is not null, cancellationToken);
		if (root.TryGetProperty("error", out _))
		{
			throw new InvalidOperationException($"Tiller MCP call {path} returned an RPC error. No result was accepted.");
		}

		if (!root.TryGetProperty("result", out JsonElement result))
		{
			throw new InvalidOperationException($"Tiller MCP call {path} returned no result. No result was accepted.");
		}

		if (result.TryGetProperty("isError", out JsonElement isError) && isError.ValueKind == JsonValueKind.True)
		{
			throw new InvalidOperationException($"Tiller MCP tool {path} reported an error. No result was accepted.");
		}

		if (result.TryGetProperty("structuredContent", out JsonElement structured)
			&& structured.ValueKind == JsonValueKind.Object)
		{
			return structured;
		}

		if (result.TryGetProperty("content", out JsonElement content)
			&& content.ValueKind == JsonValueKind.Array
			&& content.GetArrayLength() > 0
			&& content[0].TryGetProperty("text", out JsonElement text)
			&& text.ValueKind == JsonValueKind.String)
		{
			using JsonDocument document = JsonDocument.Parse(text.GetString() ?? "{}");
			return document.RootElement.Clone();
		}

		throw new InvalidOperationException($"Tiller MCP call {path} returned an unusable payload. No result was accepted.");
	}

	public async Task<IReadOnlyList<TillerSpreadsheet>> ListSpreadsheetsAsync(CancellationToken cancellationToken)
	{
		const string cacheKey = $"{CacheKeyPrefix}spreadsheets";
		if (memoryCache.TryGetValue(cacheKey, out List<TillerSpreadsheet>? cached) && cached is not null)
		{
			return cached;
		}

		JsonElement root = await InvokeAsync("tools.tiller.list_spreadsheets", new { }, cancellationToken);
		List<TillerSpreadsheet> sheets = root.TryGetProperty("spreadsheets", out JsonElement items)
			? items.EnumerateArray().Select(e => new TillerSpreadsheet(
				e.GetProperty("spreadsheet_id").GetString() ?? string.Empty,
				e.GetProperty("spreadsheetTitle").GetString() ?? string.Empty,
				e.TryGetProperty("editable", out JsonElement editable) && editable.GetBoolean(),
				e.TryGetProperty("backendAutoFill", out JsonElement fill) && fill.GetBoolean(),
				e.TryGetProperty("accountCount", out JsonElement count) && count.TryGetInt32(out int n) ? n : 0)).ToList()
			: [];

		memoryCache.Set(cacheKey, sheets, TimeSpan.FromSeconds(options.CacheTtlSeconds));
		return sheets;
	}

	public async Task<IReadOnlyList<TillerAccount>> ListAccountsAsync(CancellationToken cancellationToken)
	{
		const string cacheKey = $"{CacheKeyPrefix}accounts";
		if (memoryCache.TryGetValue(cacheKey, out List<TillerAccount>? cached) && cached is not null)
		{
			return cached;
		}

		JsonElement root = await InvokeAsync("tools.tiller.list_accounts", new { }, cancellationToken);
		List<TillerAccount> accounts = root.TryGetProperty("accounts", out JsonElement items)
			? items.EnumerateArray().Select(e => new TillerAccount(
				e.GetProperty("accountId").GetString() ?? string.Empty,
				(e.TryGetProperty("nickName", out JsonElement nick) ? nick.GetString() : null)
					?? (e.TryGetProperty("aggregatorName", out JsonElement agg) ? agg.GetString() : null)
					?? string.Empty,
				e.TryGetProperty("institution", out JsonElement inst) ? inst.GetString() ?? string.Empty : string.Empty,
				e.TryGetProperty("accountType", out JsonElement type) ? type.GetString() ?? string.Empty : string.Empty,
				e.TryGetProperty("class", out JsonElement cls) ? cls.GetString() ?? string.Empty : string.Empty)).ToList()
			: [];

		memoryCache.Set(cacheKey, accounts, TimeSpan.FromSeconds(options.CacheTtlSeconds));
		return accounts;
	}

	public async Task<IReadOnlyList<TillerCategory>> ListCategoriesAsync(string spreadsheetId, CancellationToken cancellationToken)
	{
		RequireSpreadsheet(spreadsheetId);
		string cacheKey = $"{CacheKeyPrefix}categories:{spreadsheetId}";
		if (memoryCache.TryGetValue(cacheKey, out List<TillerCategory>? cached) && cached is not null)
		{
			return cached;
		}

		JsonElement root = await InvokeAsync(
			"tools.tiller.list_categories", new { spreadsheetId }, cancellationToken);
		List<TillerCategory> categories = root.TryGetProperty("categories", out JsonElement items)
			? items.EnumerateArray().Select(e => new TillerCategory(
				e.TryGetProperty("category", out JsonElement name) ? name.GetString() ?? string.Empty : string.Empty,
				e.TryGetProperty("group", out JsonElement group) ? group.GetString() ?? string.Empty : string.Empty,
				e.TryGetProperty("type", out JsonElement type) ? type.GetString() ?? string.Empty : string.Empty,
				e.TryGetProperty("hideFromReports", out JsonElement hide) && hide.GetBoolean())).ToList()
			: [];

		memoryCache.Set(cacheKey, categories, TimeSpan.FromSeconds(options.CacheTtlSeconds));
		return categories;
	}

	public async Task<TillerSheetDescription> DescribeSheetAsync(string spreadsheetId, string sheet, CancellationToken cancellationToken)
	{
		RequireSpreadsheet(spreadsheetId);
		if (!DescribableSheets.Contains(sheet))
		{
			throw new ArgumentException(
				$"Sheet '{sheet}' is not in the read-only allowlist (Transactions, Categories). No request was sent.",
				nameof(sheet));
		}

		string cacheKey = $"{CacheKeyPrefix}describe:{spreadsheetId}:{sheet}";
		if (memoryCache.TryGetValue(cacheKey, out TillerSheetDescription? cached) && cached is not null)
		{
			return cached;
		}

		JsonElement root = await InvokeAsync(
			"tools.tiller.describe_sheet", new { spreadsheetId, sheet }, cancellationToken);
		List<string> columns = root.TryGetProperty("columns", out JsonElement items)
			? items.EnumerateArray()
				.Select(e => e.TryGetProperty("header", out JsonElement header) ? header.GetString() ?? string.Empty : string.Empty)
				.Where(h => h.Length > 0).ToList()
			: [];
		int rowCount = root.TryGetProperty("rowCount", out JsonElement count) && count.TryGetInt32(out int n) ? n : 0;
		TillerSheetDescription description = new(sheet, columns, rowCount);

		memoryCache.Set(cacheKey, description, TimeSpan.FromSeconds(options.CacheTtlSeconds));
		return description;
	}

	public async Task<TillerTransactionPage> QueryTransactionsAsync(
		string spreadsheetId,
		TillerTransactionQuery query,
		CancellationToken cancellationToken)
	{
		RequireSpreadsheet(spreadsheetId);

		Dictionary<string, object?> queryObject = new();
		Dictionary<string, object?> dateFilter = new();
		if (query.DateFrom.HasValue)
		{
			dateFilter["$gte"] = query.DateFrom.Value.ToString("yyyy-MM-dd");
		}

		if (query.DateTo.HasValue)
		{
			dateFilter["$lte"] = query.DateTo.Value.ToString("yyyy-MM-dd");
		}

		if (dateFilter.Count > 0)
		{
			queryObject["filter"] = new Dictionary<string, object?> { ["date"] = dateFilter };
		}

		if (!string.IsNullOrWhiteSpace(query.Search))
		{
			queryObject["search"] = query.Search;
		}

		if (!string.IsNullOrWhiteSpace(query.SortField))
		{
			queryObject["sort"] = new[] { new { field = query.SortField, direction = query.SortDirection ?? "desc" } };
		}

		int limit = Math.Clamp(query.Limit <= 0 ? options.DefaultPageSize : query.Limit, 1, options.MaxPageSize);
		queryObject["limit"] = limit;
		queryObject["offset"] = Math.Max(0, query.Offset);

		// The provider returns at most `limit` rows newest-first by default;
		// a short page (fewer than requested) marks the end of history.
		JsonElement root = await InvokeAsync(
			"tools.tiller.query_transactions",
			new { spreadsheetId, query = queryObject },
			cancellationToken);
		List<TillerTransactionRow> rows = root.TryGetProperty("transactions", out JsonElement items)
			? items.EnumerateArray().Select(ParseRow).Where(r => r is not null).Select(r => r!).ToList()
			: [];

		// Provider-reported total for pagination consistency guards (missing = unknown).
		int? totalAvailable = null;
		if (root.TryGetProperty("audit", out JsonElement audit) &&
			audit.TryGetProperty("pipeline", out JsonElement pipeline) &&
			pipeline.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement step in pipeline.EnumerateArray())
			{
				if (step.TryGetProperty("kind", out JsonElement kind) &&
					kind.GetString() == "load" &&
					step.TryGetProperty("rowsAvailable", out JsonElement total) &&
					total.TryGetInt32(out int n) && n >= 0)
				{
					totalAvailable = n;
					break;
				}
			}
		}

		return new TillerTransactionPage(rows, rows.Count >= limit, totalAvailable);
	}

	// Rows carry every column under its exact sheet header text (e.g.
	// "Transaction ID"); unlisted/extra columns never break parsing. Status
	// is absent from the live Transactions columns, so a missing/blank
	// status reads as posted — pending applies only when explicitly stated.
	private static TillerTransactionRow? ParseRow(JsonElement element)
	{
		string? transactionId = GetString(element, "Transaction ID") ?? GetString(element, "transactionId");
		string? dateText = GetString(element, "Date") ?? GetString(element, "date");
		if (string.IsNullOrWhiteSpace(transactionId) || !DateOnly.TryParse(dateText, out DateOnly date))
		{
			return null;
		}

		decimal amount = 0m;
		if (element.TryGetProperty("Amount", out JsonElement amountElement) && amountElement.ValueKind == JsonValueKind.Number)
		{
			amount = amountElement.GetDecimal();
		}

		string status = GetString(element, "Status") ?? GetString(element, "status") ?? "posted";
		if (string.IsNullOrWhiteSpace(status))
		{
			status = "posted";
		}

		return new TillerTransactionRow(
			transactionId,
			date,
			GetString(element, "Description") ?? GetString(element, "description") ?? string.Empty,
			amount,
			GetString(element, "Category") ?? GetString(element, "category"),
			GetString(element, "Account") ?? GetString(element, "account") ?? string.Empty,
			GetString(element, "Institution") ?? GetString(element, "institution") ?? string.Empty,
			status,
			GetString(element, "Split Group") ?? GetString(element, "splitGroupId"));
	}

	private static string? GetString(JsonElement element, string name) =>
		element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}
