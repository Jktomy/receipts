using System.Net.Http.Json;
using System.Text.Json;
using Application.Interfaces.Services;
using Application.Models.Tiller;
using Infrastructure.Tiller;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

// Native read-only Tiller transport over the ACTUALLY CONNECTED Executor
// Tiller read interface (verified live: list_spreadsheets, list_accounts,
// list_categories, describe_sheet, query_transactions — read RPCs, POST
// transport, exact account envelope).
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

	public bool IsConfigured =>
		!string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(AccountId);

	private void RequireConfigured()
	{
		if (!IsConfigured)
		{
			throw new InvalidOperationException(
				"Tiller read interface is not configured (TILLER_BASE_URL / TILLER_ACCOUNT_ID). No request was sent.");
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
	private async Task<JsonElement> InvokeAsync(string path, object input, CancellationToken cancellationToken)
	{
		RequireConfigured();
		if (!ReadPaths.Contains(path))
		{
			throw new InvalidOperationException($"Tiller tool path '{path}' is not in the read-only allowlist. No request was sent.");
		}

		logger.LogDebug("Tiller read {Path}", path);
		using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
			path, new Dictionary<string, object?> { ["accountId"] = AccountId, ["input"] = input }, cancellationToken);
		response.EnsureSuccessStatusCode();

		using JsonDocument document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken)
			?? throw new InvalidOperationException($"Tiller read {path} returned an empty body.");
		return document.RootElement.Clone();
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

		return new TillerTransactionPage(rows, rows.Count >= limit);
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
