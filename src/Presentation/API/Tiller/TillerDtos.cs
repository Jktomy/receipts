namespace API.Tiller;

// Hand-written server DTOs for the Tiller surface. Field-for-field mirrors
// of the Application models; W20 moves these into Generated during OpenAPI
// integration (spec fragment is a W20 hunk).
public sealed record TillerSpreadsheetDto(
	string SpreadsheetId,
	string Title,
	bool Editable,
	bool BackendAutoFill,
	int AccountCount);

public sealed record TillerAccountDto(
	string AccountId,
	string DisplayName,
	string Institution,
	string AccountType,
	string Class);

public sealed record TillerCategoryDto(string Name, string Group, string Type, bool HideFromReports);

public sealed record TillerConnectionStatusDto(
	bool IsConfigured,
	bool IsConnected,
	string? SelectedSpreadsheetId,
	DateTimeOffset? LastSuccessfulSyncUtc);

public sealed record TillerSelectedSpreadsheetDto(string? SelectedSpreadsheetId);

public sealed record TillerSyncStatusDto(
	string? SpreadsheetId,
	DateOnly? SyncedThroughDate,
	bool Completed,
	DateTimeOffset? LastSuccessfulSyncUtc);

public sealed record TillerSyncResultDto(
	int Inserted,
	int Updated,
	int Reconciled,
	int Duplicates,
	int Invalid,
	int Pages);

public sealed record TillerStoredTransactionDto(
	Guid Id,
	string ProviderTransactionId,
	DateOnly Date,
	string Description,
	decimal Amount,
	string? Category,
	string Account,
	string Institution,
	string Status,
	string? SplitGroupId,
	int Revision,
	string? LinkKind,
	Guid? LinkedReceiptId);

public sealed record TillerTransactionListDto(IReadOnlyList<TillerStoredTransactionDto> Rows, int Total);

public sealed record TillerAccountMappingResponse(
	Guid Id,
	Guid ReceiptsAccountId,
	string TillerAccountId,
	string TillerAccountName,
	string SpreadsheetId);

public sealed record TillerAccountMappingListDto(IReadOnlyList<TillerAccountMappingResponse> Data);

public sealed record TillerMatchCandidateDto(Guid ReceiptId, string ProviderTransactionId, int DateWithinDays);

public sealed record TillerMatchSuggestionDto(
	string Decision,
	Guid? ReceiptId,
	string? Confidence,
	IReadOnlyList<TillerMatchCandidateDto> Candidates,
	decimal? SuggestedTip = null);

public sealed record TillerLinkHistoryDto(
	string Action,
	Guid? ReceiptId,
	string? Reviewer,
	DateTimeOffset At);

public sealed record TillerLinkHistoryListDto(IReadOnlyList<TillerLinkHistoryDto> Data);

public sealed record TillerPaymentContextDto(
	string ProviderTransactionId,
	DateOnly Date,
	string Description,
	decimal Amount,
	string Account,
	string? Category,
	string? LinkKind);

public sealed record TillerSyncEventDto(
	Guid Id,
	string EventType,
	bool Success,
	int Inserted,
	int Updated,
	int Reconciled,
	int Duplicates,
	string? ErrorMessage,
	DateTimeOffset OccurredAt);

public sealed record TillerSyncEventListDto(IReadOnlyList<TillerSyncEventDto> Rows, int Total);

public sealed record TillerProductFactDto(
	string Description,
	decimal Quantity,
	decimal UnitPrice,
	decimal Total,
	string Store,
	Guid ReceiptId,
	DateOnly ReceiptDate);

public sealed record TillerBasketPaymentDto(
	string ProviderTransactionId,
	string Account,
	DateOnly Date,
	string? Category);

public sealed record TillerBasketDto(
	Guid ReceiptId,
	string Store,
	DateOnly Date,
	TillerBasketPaymentDto? Payment);

public sealed record TillerStoreRollupDto(string Store, int ReceiptCount, decimal Total);

public sealed record TillerUnmatchedTransactionDto(
	string ProviderTransactionId,
	DateOnly Date,
	string Description,
	decimal Amount,
	string Account,
	string? Category);

public sealed record TillerAnalyticsDto(
	IReadOnlyList<TillerProductFactDto> Products,
	IReadOnlyList<TillerBasketDto> Baskets,
	IReadOnlyList<TillerStoreRollupDto> Stores,
	IReadOnlyList<TillerUnmatchedTransactionDto> UnmatchedTransactions,
	IReadOnlyList<string> Alerts,
	int CapturedReceiptCount,
	decimal BankSpendingTotal,
	decimal CapturedReceiptsTotal,
	string CapturedReceiptsNote);

// Request bodies
public sealed record SelectTillerSpreadsheetRequest(string SpreadsheetId);

public sealed record SyncTillerRequest(string Mode = "full");

public sealed record CreateTillerAccountMappingRequest(
	Guid ReceiptsAccountId,
	string TillerAccountId,
	string TillerAccountName,
	string SpreadsheetId);

public sealed record UpdateTillerAccountMappingRequest(
	string TillerAccountId,
	string TillerAccountName,
	string SpreadsheetId);

public sealed record ConfirmTillerLinkRequest(Guid ReceiptId, string? Reviewer);

public sealed record UnlinkTillerLinkRequest(string? Reviewer);
