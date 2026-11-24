namespace Application.Models.Tiller;

// Native read-only Tiller budget-surface DTOs, mapped 1:1 from the ACTUALLY
// CONNECTED Executor Tiller read interface (verified live 2026-11-23:
// list_spreadsheets/describe_sheet/list_categories/list_accounts handshake,
// zero mutations). No field here is ever written back upstream. Financial
// rows stay machine-side; only counts and status projections reach sync
// logs or chat.

public sealed record TillerSpreadsheet(
	string SpreadsheetId,
	string Title,
	bool Editable,
	bool BackendAutoFill,
	int AccountCount);

public sealed record TillerAccount(
	string AccountId,
	string DisplayName,
	string Institution,
	string AccountType,
	string Class);

public sealed record TillerCategory(
	string Name,
	string Group,
	string Type,
	bool HideFromReports);

public sealed record TillerTransactionRow(
	string TransactionId,
	DateOnly Date,
	string Description,
	decimal Amount,
	string? Category,
	string Account,
	string Institution,
	string Status,
	string? SplitGroupId);

public sealed record TillerSheetDescription(
	string Sheet,
	IReadOnlyList<string> Columns,
	int RowCount);

public sealed record TillerTransactionQuery(
	DateOnly? DateFrom,
	DateOnly? DateTo,
	string? Search,
	string? SortField,
	string? SortDirection,
	int Limit,
	int Offset);

public sealed record TillerTransactionPage(
	IReadOnlyList<TillerTransactionRow> Rows,
	bool Truncated,
	int? TotalAvailable = null);
