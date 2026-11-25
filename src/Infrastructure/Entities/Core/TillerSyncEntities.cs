namespace Infrastructure.Entities.Core;

// Local cache of one Tiller transaction row. Stable identity is the
// provider transactionId; spreadsheet row numbers are never IDs. Rows that
// were superseded (pending->posted ID change) are retained with
// SupersededBy set so history stays auditable.
public class TillerTransactionEntity
{
	public Guid Id { get; set; }
	public string ProviderTransactionId { get; set; } = string.Empty;
	public string SpreadsheetId { get; set; } = string.Empty;
	public DateOnly Date { get; set; }
	public string Description { get; set; } = string.Empty;
	public decimal Amount { get; set; }
	public string? Category { get; set; }
	public string Account { get; set; } = string.Empty;
	public string Institution { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;
	public string? SplitGroupId { get; set; }
	public int Revision { get; set; }
	public string? SupersededBy { get; set; }
	public DateTimeOffset CreatedAt { get; set; }
	public DateTimeOffset UpdatedAt { get; set; }
}

// Every provider ID ever observed: canonical records plus remembered
// duplicates/retired IDs, so a re-delivered row can never create a record.
public class TillerKnownIdEntity
{
	public Guid Id { get; set; }
	public string ProviderTransactionId { get; set; } = string.Empty;
	public string SpreadsheetId { get; set; } = string.Empty;
	public string? CanonicalProviderTransactionId { get; set; }
	public DateTimeOffset SeenAt { get; set; }
}

// Durable resume point per spreadsheet. Completed=false means a run was
// interrupted; the next run replays source history by stable transaction ID.
// Successful checkpoints report the source date, not an edit timestamp.
public class TillerCheckpointEntity
{
	public Guid Id { get; set; }
	public string SpreadsheetId { get; set; } = string.Empty;
	public DateOnly? SyncedThroughDate { get; set; }
	public bool Completed { get; set; }
	public DateTimeOffset UpdatedAt { get; set; }
}

// Append-only log of sync attempts (full/incremental, success/failure with
// counts). Backs the status page; derived locally, performs no live calls.
public class TillerSyncEventEntity
{
	public Guid Id { get; set; }
	public string SpreadsheetId { get; set; } = string.Empty;
	public string EventType { get; set; } = string.Empty;
	public bool Success { get; set; }
	public int Inserted { get; set; }
	public int Updated { get; set; }
	public int Reconciled { get; set; }
	public int Duplicates { get; set; }
	public string? ErrorMessage { get; set; }
	public DateTimeOffset OccurredAt { get; set; }
}

// Receipt <-> Tiller row link. Auto links come from the deterministic
// matcher; manual links record an explicit human confirm (slice 4 owns the
// matching rules — this table only persists their outcomes).
public class TillerReceiptLinkEntity
{
	public Guid Id { get; set; }
	public Guid ReceiptId { get; set; }
	public string ProviderTransactionId { get; set; } = string.Empty;
	public string LinkKind { get; set; } = "auto";
	public string? Reviewer { get; set; }
	public DateTimeOffset CreatedAt { get; set; }
	public DateTimeOffset? SupersededAt { get; set; }
}

// Reviewer history for confirm/unlink/auto-link actions. Links can be
// removed; this log cannot — it is the audit trail for human overlays.
public class TillerLinkEventEntity
{
	public Guid Id { get; set; }
	public string ProviderTransactionId { get; set; } = string.Empty;
	public string Action { get; set; } = string.Empty;
	public Guid? ReceiptId { get; set; }
	public string? Reviewer { get; set; }
	public DateTimeOffset At { get; set; }
}
