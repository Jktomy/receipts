namespace Infrastructure.Entities.Core;

// Single-row table holding the operator's chosen readable Tiller
// spreadsheet. Selection is validated against the live read list before
// storage, so no out-of-scope or non-editable spreadsheet can persist here.
public class TillerSelectedSpreadsheetEntity
{
	public Guid Id { get; set; }
	public string SpreadsheetId { get; set; } = string.Empty;
	public string? ExecutorMetadataJson { get; set; }
	public DateTimeOffset UpdatedAt { get; set; }
}

// Maps one local receipt account to one Tiller account within a single
// spreadsheet. A mapping can never move between spreadsheets on update:
// re-map explicitly instead of silently re-pointing history.
public class TillerAccountMappingEntity
{
	public Guid Id { get; set; }
	public Guid ReceiptsAccountId { get; set; }
	public string TillerAccountId { get; set; } = string.Empty;
	public string TillerAccountName { get; set; } = string.Empty;
	public string SpreadsheetId { get; set; } = string.Empty;
	public DateTimeOffset CreatedAt { get; set; }
	public DateTimeOffset UpdatedAt { get; set; }
	public virtual AccountEntity? Account { get; set; }
}
