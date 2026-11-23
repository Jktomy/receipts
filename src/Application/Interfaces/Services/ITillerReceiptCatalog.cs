using Application.Models.Tiller;

namespace Application.Interfaces.Services;

// Read-only receipt catalog for Tiller. Returns purchase facts (merchant,
// date, line-item total) plus whether every line item is human-verified.
// Only all-verified receipts may feed matching or product analytics.
public interface ITillerReceiptCatalog
{
	Task<IReadOnlyList<TillerReceiptSnapshot>> GetReceiptSnapshotsAsync(CancellationToken cancellationToken);

	Task<TillerReceiptSnapshot?> GetReceiptSnapshotAsync(Guid receiptId, CancellationToken cancellationToken);

	Task<IReadOnlyList<TillerReceiptLine>> GetVerifiedReceiptLinesAsync(CancellationToken cancellationToken);
}
