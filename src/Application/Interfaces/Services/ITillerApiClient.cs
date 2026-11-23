using Application.Models.Tiller;

namespace Application.Interfaces.Services;

// Read-only Tiller boundary for Tracer. This interface deliberately exposes
// ONLY the five budget-surface read paths (spreadsheets, accounts,
// categories, sheet description, transaction query). There is intentionally
// no update/refresh/delete/holdings/balance method: adding one would fail
// the read-only contract test in TillerApiClientTests.
public interface ITillerApiClient
{
	bool IsConfigured { get; }

	Task<IReadOnlyList<TillerSpreadsheet>> ListSpreadsheetsAsync(CancellationToken cancellationToken);

	Task<IReadOnlyList<TillerAccount>> ListAccountsAsync(CancellationToken cancellationToken);

	Task<IReadOnlyList<TillerCategory>> ListCategoriesAsync(string spreadsheetId, CancellationToken cancellationToken);

	Task<TillerSheetDescription> DescribeSheetAsync(string spreadsheetId, string sheet, CancellationToken cancellationToken);

	Task<TillerTransactionPage> QueryTransactionsAsync(
		string spreadsheetId,
		TillerTransactionQuery query,
		CancellationToken cancellationToken);
}
