using System.Net;
using System.Reflection;
using System.Text.Json;
using Application.Interfaces.Services;
using Application.Models.Tiller;
using FluentAssertions;
using Infrastructure.Services;
using Infrastructure.Tiller;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Infrastructure.Tests.Services;

// Transport boundary, verified against the ACTUALLY CONNECTED Executor
// Tiller read interface (live handshake 2026-11-23, zero mutations). The
// client may ONLY invoke the five budget-surface tool paths via MCP
// streamable-HTTP tools/call (POST {base}/mcp, Bearer from TILLER_TOKEN_FILE)
// with the exact configured account envelope. There is no update, delete,
// category-edit, holdings, balance-history, or refresh path anywhere here.
public class TillerApiClientTests
{
	private sealed class SpyHandler : HttpMessageHandler
	{
		public List<(HttpMethod Method, string? Path, string Body)> Calls { get; } = [];
		private readonly string _payload;

		public SpyHandler(string payload = "{}")
		{
			_payload = payload;
		}

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
			Calls.Add((request.Method, request.RequestUri?.AbsolutePath, body));
			string payload = body.Contains("\"method\":\"initialize\"")
				? "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-06-18\"}}"
				: body.Contains("\"method\":\"notifications/initialized\"")
					? "{}"
					: _payload.Contains("\"spreadsheets\"") || _payload.Contains("\"accounts\"") || _payload.Contains("\"transactions\"") || _payload == "{}"
						? "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"content\":[{\"type\":\"text\",\"text\":" + System.Text.Json.JsonSerializer.Serialize(_payload) + "}]}}"
						: _payload;
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
			};
		}
	}

	private static TillerApiClient CreateClient(
		HttpMessageHandler handler,
		string? baseUrl = "https://executor-gateway.local/",
		string? accountId = "acc_test_tiller",
		IMemoryCache? cache = null)
	{
		HttpClient httpClient = new(handler);
		if (baseUrl is not null)
		{
			httpClient.BaseAddress = new Uri(baseUrl);
		}

		cache ??= new MemoryCache(new MemoryCacheOptions());

		Dictionary<string, string?> configValues = new();
		string tokenFile = Path.Combine(Path.GetTempPath(), $"tiller-old-{Guid.NewGuid():N}.txt");
		File.WriteAllText(tokenFile, "synthetic-test-bearer-token");
		configValues["TILLER_TOKEN_FILE"] = tokenFile;
		if (baseUrl is not null)
		{
			configValues["TILLER_BASE_URL"] = baseUrl;
		}

		if (accountId is not null)
		{
			configValues["TILLER_ACCOUNT_ID"] = accountId;
		}

		IConfiguration configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(configValues)
			.Build();

		return new TillerApiClient(
			httpClient,
			cache,
			configuration,
			new TillerClientOptions(),
			Mock.Of<ILogger<TillerApiClient>>());
	}

	[Fact]
	public void ReadInterface_ExposesNoWriteOrRefreshOperations()
	{
		// Arrange: the read-only contract itself must be incapable of mutation.
		string[] forbidden = ["Create", "Update", "Delete", "Add", "Remove", "Refresh", "Push", "Put", "Sync", "Write", "Rename", "Holding", "Balance"];

		// Act (property accessors are not operations)
		MethodInfo[] methods = typeof(ITillerApiClient).GetMethods().Where(m => !m.IsSpecialName).ToArray();

		// Assert: operation names must not START with a mutation verb.
		methods.Should().NotBeEmpty();
		foreach (MethodInfo method in methods)
		{
			forbidden.Should().NotContain(f => method.Name.StartsWith(f, StringComparison.OrdinalIgnoreCase),
				$"ITillerApiClient.{method.Name} must not exist on a read-only interface");
			method.ReturnType.IsGenericType.Should().BeTrue();
		}
	}

	[Fact]
	public void Allowlist_IsExactlyTheFiveConnectedReadPaths()
	{
		// The exact tool paths of the connected Executor Tiller interface —
		// nothing else may ever leave this module.
		TillerApiClient.ReadPaths.Should().BeEquivalentTo(
			"tools.tiller.list_spreadsheets",
			"tools.tiller.list_accounts",
			"tools.tiller.list_categories",
			"tools.tiller.describe_sheet",
			"tools.tiller.query_transactions");
	}

	[Fact]
	public async Task ListSpreadsheets_InvokesReadRpcWithBoundAccountEnvelope()
	{
		// Arrange: live wire shape (snake_case envelope).
		const string json = """{"spreadsheets":[{"spreadsheet_id":"sheet-1","spreadsheetTitle":"Household","backendAutoFill":true,"editable":true,"accountCount":24}],"count":1}""";
		SpyHandler spy = new(json);
		TillerApiClient client = CreateClient(spy);

		// Act
		IReadOnlyList<TillerSpreadsheet> sheets = await client.ListSpreadsheetsAsync(CancellationToken.None);

		// Assert
		sheets.Should().ContainSingle().Which.Should().Be(
			new TillerSpreadsheet("sheet-1", "Household", true, true, 24));
		spy.Calls.Should().HaveCount(3, "initialize + notifications/initialized + tools/call");
		spy.Calls[2].Method.Should().Be(HttpMethod.Post);
		spy.Calls[2].Path.Should().Be("/mcp");
		JsonDocument envelope = JsonDocument.Parse(spy.Calls[2].Body);
		envelope.RootElement.GetProperty("method").GetString().Should().Be("tools/call");
		envelope.RootElement.GetProperty("params").GetProperty("name").GetString().Should().Be("tools.tiller.list_spreadsheets");
		envelope.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("accountId").GetString().Should().Be("acc_test_tiller");
	}

	[Fact]
	public async Task ListAccounts_MapsLiveAccountShape()
	{
		// Arrange: live wire shape (display = nickName ?? aggregatorName).
		const string json = """{"accounts":[{"accountId":"acc-x1","nickName":"Rewards Checking","aggregatorName":"Rewards Checking","institution":"Amex","accountType":"checking","class":"Asset"}],"count":1}""";
		SpyHandler spy = new(json);
		TillerApiClient client = CreateClient(spy);

		// Act
		IReadOnlyList<TillerAccount> accounts = await client.ListAccountsAsync(CancellationToken.None);

		// Assert
		accounts.Should().ContainSingle().Which.Should().Be(
			new TillerAccount("acc-x1", "Rewards Checking", "Amex", "checking", "Asset"));
		spy.Calls.Should().HaveCount(3, "initialize + notifications/initialized + tools/call");
		spy.Calls[2].Method.Should().Be(HttpMethod.Post);
		spy.Calls[2].Path.Should().Be("/mcp");
		JsonDocument accountEnvelope = JsonDocument.Parse(spy.Calls[2].Body);
		accountEnvelope.RootElement.GetProperty("params").GetProperty("name").GetString().Should().Be("tools.tiller.list_accounts");
	}

	[Fact]
	public async Task QueryTransactions_BuildsExactProviderVocabulary()
	{
		// Arrange: live wire shape (header-text row keys, transaction array).
		const string json = """{"transactions":[{"Transaction ID":"t-1","Date":"2026-11-01","Description":"WHOLEFDS MKT","Amount":-9.99,"Category":"Groceries","Account":"Chase Checking","Institution":"Chase"}],"count":1}""";
		SpyHandler spy = new(json);
		TillerApiClient client = CreateClient(spy);

		// Act
		TillerTransactionPage page = await client.QueryTransactionsAsync(
			"sheet-1",
			new TillerTransactionQuery(
				DateFrom: new DateOnly(2026, 11, 1),
				DateTo: null,
				Search: null,
				SortField: "date",
				SortDirection: "asc",
				Limit: 100,
				Offset: 0),
			CancellationToken.None);

		// Assert
		page.Rows.Should().ContainSingle().Which.TransactionId.Should().Be("t-1");
		page.Rows[0].Status.Should().Be("posted", "status is absent from live columns and defaults to posted");
		page.Truncated.Should().BeFalse("a short page marks the end of history");
		spy.Calls.Should().HaveCount(3, "initialize + notifications/initialized + tools/call");
		spy.Calls[2].Method.Should().Be(HttpMethod.Post);
		spy.Calls[2].Path.Should().Be("/mcp");
		JsonDocument body = JsonDocument.Parse(spy.Calls[2].Body);
		body.RootElement.GetProperty("method").GetString().Should().Be("tools/call");
		body.RootElement.GetProperty("params").GetProperty("name").GetString().Should().Be("tools.tiller.query_transactions");
		body.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("accountId").GetString().Should().Be("acc_test_tiller");
		body.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("input").GetProperty("spreadsheetId").GetString().Should().Be("sheet-1");
		JsonElement query = body.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("input").GetProperty("query");
		query.GetProperty("filter").GetProperty("date").GetProperty("$gte").GetString().Should().Be("2026-11-01");
		query.GetProperty("sort")[0].GetProperty("field").GetString().Should().Be("date");
		query.GetProperty("sort")[0].GetProperty("direction").GetString().Should().Be("asc");
		query.GetProperty("limit").GetInt32().Should().Be(100);
	}

	[Fact]
	public async Task QueryTransactions_MissingStatusDefaultsToPosted()
	{
		// Arrange: live rows carry no Status column.
		const string json = """{"transactions":[{"Transaction ID":"p-1","Date":"2026-11-06","Description":"WHOLEFDS MKT","Amount":-15.00,"Account":"Chase","Institution":"Chase"}],"count":1}""";
		SpyHandler spy = new(json);
		TillerApiClient client = CreateClient(spy);

		// Act
		TillerTransactionPage page = await client.QueryTransactionsAsync(
			"sheet-1",
			new TillerTransactionQuery(null, null, null, null, null, 100, 0),
			CancellationToken.None);

		// Assert
		page.Rows.Should().ContainSingle().Which.Status.Should().Be("posted");
	}

	[Fact]
	public async Task EmptySpreadsheetId_ThrowsBeforeAnyTransport()
	{
		// Arrange
		SpyHandler spy = new();
		TillerApiClient client = CreateClient(spy);

		// Act
		Func<Task> categories = () => client.ListCategoriesAsync("", CancellationToken.None);
		Func<Task> describe = () => client.DescribeSheetAsync("  ", "Transactions", CancellationToken.None);
		Func<Task> query = () => client.QueryTransactionsAsync(
			"",
			new TillerTransactionQuery(null, null, null, null, null, 100, 0),
			CancellationToken.None);

		// Assert
		await categories.Should().ThrowAsync<ArgumentException>();
		await describe.Should().ThrowAsync<ArgumentException>();
		await query.Should().ThrowAsync<ArgumentException>();
		spy.Calls.Should().BeEmpty("out-of-scope spreadsheet IDs are rejected before transport");
	}

	[Fact]
	public async Task NonBudgetSheetDescribe_ThrowsBeforeAnyTransport()
	{
		// Arrange
		SpyHandler spy = new();
		TillerApiClient client = CreateClient(spy);

		// Act
		Func<Task> balance = () => client.DescribeSheetAsync("sheet-1", "Balance History", CancellationToken.None);
		Func<Task> holdings = () => client.DescribeSheetAsync("sheet-1", "Holdings", CancellationToken.None);

		// Assert
		await balance.Should().ThrowAsync<ArgumentException>();
		await holdings.Should().ThrowAsync<ArgumentException>();
		spy.Calls.Should().BeEmpty("holdings and balance sheets are outside the read allowlist");
	}

	[Fact]
	public async Task UnconfiguredClient_ReportsNotConfiguredAndSendsNothing()
	{
		// Arrange: no base URL and no account — the read interface is not wired yet.
		SpyHandler spy = new();
		TillerApiClient client = CreateClient(spy, baseUrl: null, accountId: null);

		// Act
		bool configured = client.IsConfigured;
		Func<Task> call = () => client.ListSpreadsheetsAsync(CancellationToken.None);

		// Assert
		configured.Should().BeFalse();
		await call.Should().ThrowAsync<InvalidOperationException>();
		spy.Calls.Should().BeEmpty();
	}
}
