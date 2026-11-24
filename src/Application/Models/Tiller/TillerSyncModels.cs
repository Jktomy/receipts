namespace Application.Models.Tiller;

public enum TillerSyncEventType
{
	Full,
	Incremental,
}

public sealed record TillerSyncResult(
	int Inserted,
	int Updated,
	int Reconciled,
	int Duplicates,
	int Invalid,
	int Pages);

public sealed record TillerSyncStatus(
	string? SpreadsheetId,
	DateOnly? SyncedThroughDate,
	bool Completed,
	DateTimeOffset? LastSuccessfulSyncUtc);
