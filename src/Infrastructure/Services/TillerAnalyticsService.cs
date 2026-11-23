using Application.Interfaces.Services;
using Application.Models.Tiller;
using Infrastructure.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

// Purchase-intelligence analytics over verified receipt lines + cached
// Tiller rows. Verified lines are the SOLE source of product facts; Tiller
// rows supply payment context only. Bank spending and captured-receipt
// totals are reported in separate buckets with an explicit scope note.
public class TillerAnalyticsService(
	IDbContextFactory<ApplicationDbContext> contextFactory,
	ITillerReceiptCatalog receiptCatalog) : ITillerAnalyticsService
{
	private const string CapturedNote =
		"Captured and reviewed receipts only: a subset of bank spending, never a completeness grade.";

	public async Task<TillerAnalyticsResult> GetAnalyticsAsync(CancellationToken cancellationToken)
	{
		IReadOnlyList<TillerReceiptLine> lines =
			await receiptCatalog.GetVerifiedReceiptLinesAsync(cancellationToken);
		IReadOnlyList<TillerReceiptSnapshot> snapshots =
			await receiptCatalog.GetReceiptSnapshotsAsync(cancellationToken);

		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		List<TillerTransactionEntity> txns = await context.Set<TillerTransactionEntity>()
			.AsNoTracking()
			.Where(e => e.SupersededBy == null)
			.ToListAsync(cancellationToken);
		List<TillerReceiptLinkEntity> links = await context.Set<TillerReceiptLinkEntity>()
			.AsNoTracking()
			.Where(l => l.SupersededAt == null)
			.ToListAsync(cancellationToken);

		Dictionary<Guid, TillerReceiptLinkEntity> linkByReceipt = links
			.GroupBy(l => l.ReceiptId)
			.ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.CreatedAt).First());
		Dictionary<string, TillerTransactionEntity> txnByProvider = txns
			.ToDictionary(t => t.ProviderTransactionId, t => t);
		HashSet<string> linkedTxnIds = links.Select(l => l.ProviderTransactionId).ToHashSet();

		List<TillerProductFact> products = lines
			.Select(l => new TillerProductFact(
				l.Description, l.Quantity, l.UnitPrice, l.Total, l.Merchant, l.ReceiptId, l.Date))
			.ToList();

		List<TillerBasket> baskets = snapshots
			.Where(s => s.AllItemsVerified)
			.Select(s =>
			{
				linkByReceipt.TryGetValue(s.ReceiptId, out TillerReceiptLinkEntity? link);
				TillerTransactionEntity? txn = link is not null && txnByProvider.TryGetValue(link.ProviderTransactionId, out TillerTransactionEntity? t) ? t : null;
				return new TillerBasket(
					s.ReceiptId,
					s.Merchant,
					s.Date,
					txn is null ? null : new TillerBasketPayment(
						txn.ProviderTransactionId, txn.Account, txn.Date, txn.Category));
			})
			.ToList();

		List<TillerStoreRollup> stores = lines
			.GroupBy(l => l.Merchant)
			.Select(g => new TillerStoreRollup(
				g.Key,
				g.Select(l => l.ReceiptId).Distinct().Count(),
				g.Sum(l => l.Total)))
			.ToList();

		List<TillerUnmatchedTransaction> unmatched = txns
			.Where(t => !linkedTxnIds.Contains(t.ProviderTransactionId))
			.Select(t => new TillerUnmatchedTransaction(
				t.ProviderTransactionId, t.Date, t.Description, t.Amount, t.Account, t.Category))
			.ToList();

		return new TillerAnalyticsResult(
			products,
			baskets,
			stores,
			unmatched,
			[],
			snapshots.Count(s => s.AllItemsVerified),
			txns.Sum(t => t.Amount),
			lines.Sum(l => l.Total),
			CapturedNote);
	}
}
