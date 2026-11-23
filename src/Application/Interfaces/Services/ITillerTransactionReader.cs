using Application.Models.Tiller;

namespace Application.Interfaces.Services;

// Read-only access to Tracer's LOCAL Tiller cache (synced rows, sync
// events). Never touches upstream; all filtering/paging happens here so
// query handlers stay thin.
public sealed record TillerStoredTransaction(
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

public sealed record TillerTransactionListResult(
	IReadOnlyList<TillerStoredTransaction> Rows,
	int Total);

public sealed record TillerStoredSyncEvent(
	Guid Id,
	string EventType,
	bool Success,
	int Inserted,
	int Updated,
	int Reconciled,
	int Duplicates,
	string? ErrorMessage,
	DateTimeOffset OccurredAt);

public sealed record TillerSyncEventListResult(
	IReadOnlyList<TillerStoredSyncEvent> Rows,
	int Total);

public interface ITillerTransactionReader
{
	Task<TillerTransactionListResult> GetTransactionsAsync(
		int offset,
		int limit,
		string? sortBy,
		string? sortDirection,
		string? search,
		bool unlinkedOnly,
		string? account,
		CancellationToken cancellationToken);

	Task<TillerSyncEventListResult> GetSyncEventsAsync(
		int offset,
		int limit,
		bool? success,
		CancellationToken cancellationToken);
}
