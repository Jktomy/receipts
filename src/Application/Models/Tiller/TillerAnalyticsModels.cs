namespace Application.Models.Tiller;

// Purchase-intelligence analytics for Tracer. HARD BOUNDARIES:
// - Product facts (description, quantity, unit price, totals) come ONLY
//   from human-verified receipt lines. Tiller rows NEVER manufacture
//   products, brands, or quantities.
// - Missing receipts are normal: unmatched transactions are neutral
//   context. No alerts, scores, nags, flags, or required tasks.
// - Bank spending and captured-receipt analyses are separate buckets with
//   an explicit scope note; they are never mixed.
public sealed record TillerProductFact(
	string Description,
	decimal Quantity,
	decimal UnitPrice,
	decimal Total,
	string Store,
	Guid ReceiptId,
	DateOnly ReceiptDate);

public sealed record TillerBasket(
	Guid ReceiptId,
	string Store,
	DateOnly Date,
	TillerBasketPayment? Payment);

public sealed record TillerBasketPayment(
	string ProviderTransactionId,
	string Account,
	DateOnly Date,
	string? Category);

public sealed record TillerStoreRollup(
	string Store,
	int ReceiptCount,
	decimal Total);

public sealed record TillerUnmatchedTransaction(
	string ProviderTransactionId,
	DateOnly Date,
	string Description,
	decimal Amount,
	string Account,
	string? Category);

public sealed record TillerAnalyticsResult(
	IReadOnlyList<TillerProductFact> Products,
	IReadOnlyList<TillerBasket> Baskets,
	IReadOnlyList<TillerStoreRollup> Stores,
	IReadOnlyList<TillerUnmatchedTransaction> UnmatchedTransactions,
	IReadOnlyList<string> Alerts,
	int CapturedReceiptCount,
	decimal BankSpendingTotal,
	decimal CapturedReceiptsTotal,
	string CapturedReceiptsNote);
