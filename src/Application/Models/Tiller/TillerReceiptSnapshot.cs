namespace Application.Models.Tiller;

// Minimal explicit contract for the receipt facts Tiller needs. Read-only:
// the matcher/analytics never modify receipts through this contract.
public sealed record TillerReceiptSnapshot(
	Guid ReceiptId,
	string Merchant,
	DateOnly Date,
	decimal Total,
	bool AllItemsVerified,
	decimal TaxTotal = 0m,
	decimal TipTotal = 0m,
	bool TaxUnknown = false,
	decimal OtherAdjustmentTotal = 0m)
{
	// Total is the stored item subtotal, not the payment amount.
	public decimal ExpectedTotal => Total + TaxTotal + TipTotal + OtherAdjustmentTotal;
}

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
