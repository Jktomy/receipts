using Application.Interfaces.Services;
using Application.Models.Tiller;

namespace API.Tiller;

using AppMapping = Application.Models.Tiller.TillerAccountMappingDto;

// Manual mapper: Application Tiller models -> server DTOs. Explicit by
// design — no generated-mapper drift on a read-only surface.
public class TillerMapper
{
	public TillerConnectionStatusDto ToConnectionStatusDto(TillerConnectionStatus source) => new(
		source.IsConfigured, source.IsConnected, source.SelectedSpreadsheetId, source.LastSuccessfulSyncUtc);

	public TillerSyncStatusDto ToSyncStatusDto(TillerSyncStatus source) => new(
		source.SpreadsheetId, source.SyncedThroughDate, source.Completed, source.LastSuccessfulSyncUtc);

	public TillerSyncResultDto ToSyncResultDto(TillerSyncResult source) => new(
		source.Inserted, source.Updated, source.Reconciled, source.Duplicates, source.Invalid, source.Pages);

	public IReadOnlyList<TillerSpreadsheetDto> ToSpreadsheetDtos(IReadOnlyList<TillerSpreadsheet> source) =>
		source.Select(s => new TillerSpreadsheetDto(s.SpreadsheetId, s.Title, s.Editable, s.BackendAutoFill, s.AccountCount)).ToList();

	public IReadOnlyList<TillerAccountDto> ToAccountDtos(IReadOnlyList<TillerAccount> source) =>
		source.Select(a => new TillerAccountDto(a.AccountId, a.DisplayName, a.Institution, a.AccountType, a.Class)).ToList();

	public IReadOnlyList<TillerCategoryDto> ToCategoryDtos(IReadOnlyList<TillerCategory> source) =>
		source.Select(c => new TillerCategoryDto(c.Name, c.Group, c.Type, c.HideFromReports)).ToList();

	public TillerTransactionListDto ToTransactionListDto(TillerTransactionListResult source) => new(
		source.Rows.Select(r => new TillerStoredTransactionDto(
			r.Id, r.ProviderTransactionId, r.Date, r.Description, r.Amount,
			r.Category, r.Account, r.Institution, r.Status, r.SplitGroupId,
			r.Revision, r.LinkKind, r.LinkedReceiptId)).ToList(),
		source.Total);

	public TillerAccountMappingResponse ToMappingDto(AppMapping source) => new(
		source.Id, source.ReceiptsAccountId, source.TillerAccountId, source.TillerAccountName, source.SpreadsheetId);

	public TillerAccountMappingListDto ToMappingListDto(List<AppMapping> source) => new(
		source.Select(ToMappingDto).ToList());

	public TillerMatchSuggestionDto ToSuggestionDto(TillerMatchSuggestion source) => new(
		source.Decision.ToString().ToLowerInvariant(),
		source.ReceiptId,
		source.Confidence,
		source.Candidates.Select(c => new TillerMatchCandidateDto(c.ReceiptId, c.ProviderTransactionId, c.DateWithinDays)).ToList(),
		source.SuggestedTip);

	public TillerLinkHistoryListDto ToHistoryListDto(IReadOnlyList<TillerLinkHistoryEntry> source) => new(
		source.Select(h => new TillerLinkHistoryDto(h.Action, h.ReceiptId, h.Reviewer, h.At)).ToList());

	public TillerPaymentContextDto? ToPaymentContextDto(TillerPaymentContext? source) => source is null
		? null
		: new TillerPaymentContextDto(
			source.ProviderTransactionId, source.Date, source.Description, source.Amount,
			source.Account, source.Category, source.LinkKind);

	public TillerSyncEventListDto ToSyncEventListDto(TillerSyncEventListResult source) => new(
		source.Rows.Select(e => new TillerSyncEventDto(
			e.Id, e.EventType, e.Success, e.Inserted, e.Updated,
			e.Reconciled, e.Duplicates, e.ErrorMessage, e.OccurredAt)).ToList(),
		source.Total);

	public TillerAnalyticsDto ToAnalyticsDto(TillerAnalyticsResult source) => new(
		source.Products.Select(p => new TillerProductFactDto(
			p.Description, p.Quantity, p.UnitPrice, p.Total, p.Store, p.ReceiptId, p.ReceiptDate)).ToList(),
		source.Baskets.Select(b => new TillerBasketDto(
			b.ReceiptId, b.Store, b.Date,
			b.Payment is null ? null : new TillerBasketPaymentDto(
				b.Payment.ProviderTransactionId, b.Payment.Account, b.Payment.Date, b.Payment.Category))).ToList(),
		source.Stores.Select(s => new TillerStoreRollupDto(s.Store, s.ReceiptCount, s.Total)).ToList(),
		source.UnmatchedTransactions.Select(u => new TillerUnmatchedTransactionDto(
			u.ProviderTransactionId, u.Date, u.Description, u.Amount, u.Account, u.Category)).ToList(),
		source.Alerts,
		source.CapturedReceiptCount,
		source.BankSpendingTotal,
		source.CapturedReceiptsTotal,
		source.CapturedReceiptsNote);
}
