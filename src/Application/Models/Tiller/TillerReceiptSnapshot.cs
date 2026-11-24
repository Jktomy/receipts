namespace Application.Models.Tiller;

// Minimal explicit contract for the receipt facts Tiller needs. Read-only:
// the matcher/analytics never modify receipts through this contract.
public sealed record TillerReceiptSnapshot(
	Guid ReceiptId,
	string Merchant,
	DateOnly Date,
	decimal Total,
	bool AllItemsVerified);

// One human-verified line item. Only lines from all-verified receipts are
// ever returned; product facts come exclusively from these rows.
public sealed record TillerReceiptLine(
	Guid ReceiptId,
	string Merchant,
	DateOnly Date,
	string Description,
	decimal Quantity,
	decimal UnitPrice,
	decimal Total);
