namespace Application.Models.Tiller;

public enum TillerMatchDecision
{
	Unknown,
	Linked,
	Manual,
	Ambiguous,
	Taken,
	None,
	PossibleTip,
}

public sealed record TillerMatchCandidate(
	Guid ReceiptId,
	string ProviderTransactionId,
	int DateWithinDays);

public sealed record TillerMatchSuggestion(
	TillerMatchDecision Decision,
	Guid? ReceiptId,
	string? Confidence,
	IReadOnlyList<TillerMatchCandidate> Candidates,
	decimal? SuggestedTip = null);

public sealed record TillerLinkHistoryEntry(
	string Action,
	Guid? ReceiptId,
	string? Reviewer,
	DateTimeOffset At);

public sealed record TillerPaymentContext(
	string ProviderTransactionId,
	DateOnly Date,
	string Description,
	decimal Amount,
	string Account,
	string? Category,
	string? LinkKind);
