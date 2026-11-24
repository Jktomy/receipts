using Application.Models.Tiller;

namespace Application.Interfaces.Services;

// Deterministic receipt<->transaction matching. No models, no invented
// products: only human-verified receipts (every line item Verified) can
// match, and ambiguity yields neutral candidates for explicit human
// confirmation — never an auto-link.
public interface ITillerMatchingService
{
	Task<TillerMatchSuggestion> SuggestAsync(string providerTransactionId, CancellationToken cancellationToken);

	Task<TillerMatchSuggestion> SuggestForReceiptAsync(
		string providerTransactionId,
		Guid receiptId,
		CancellationToken cancellationToken);

	Task<TillerMatchSuggestion> ConfirmAsync(
		string providerTransactionId,
		Guid receiptId,
		string? reviewer,
		CancellationToken cancellationToken);

	Task<TillerMatchSuggestion> UnlinkAsync(
		string providerTransactionId,
		string? reviewer,
		CancellationToken cancellationToken);

	Task<IReadOnlyList<TillerLinkHistoryEntry>> GetHistoryAsync(
		string providerTransactionId,
		CancellationToken cancellationToken);

	Task<TillerPaymentContext?> GetPaymentContextAsync(Guid receiptId, CancellationToken cancellationToken);
}
