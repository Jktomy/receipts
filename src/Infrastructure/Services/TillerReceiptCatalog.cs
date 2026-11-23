using Application.Interfaces.Services;
using Application.Models.Tiller;
using Domain.NormalizedDescriptions;
using Infrastructure.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

// Native receipt-fact reader for Tiller. Totals come from stored line-item
// amounts; the verified flag requires every line item human-verified.
// Review state itself is owned by the normalization flows — this reader
// only reports it.
public class TillerReceiptCatalog(
	IDbContextFactory<ApplicationDbContext> contextFactory) : ITillerReceiptCatalog
{
	public async Task<IReadOnlyList<TillerReceiptSnapshot>> GetReceiptSnapshotsAsync(
		CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		List<ReceiptEntity> receipts = await context.Set<ReceiptEntity>()
			.AsNoTracking()
			.ToListAsync(cancellationToken);

		List<TillerReceiptSnapshot> snapshots = [];
		foreach (ReceiptEntity receipt in receipts)
		{
			TillerReceiptSnapshot? snapshot = await ToSnapshotAsync(context, receipt, cancellationToken);
			if (snapshot is not null)
			{
				snapshots.Add(snapshot);
			}
		}

		return snapshots;
	}

	public async Task<TillerReceiptSnapshot?> GetReceiptSnapshotAsync(
		Guid receiptId,
		CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		ReceiptEntity? receipt = await context.Set<ReceiptEntity>()
			.AsNoTracking()
			.SingleOrDefaultAsync(r => r.Id == receiptId, cancellationToken);
		if (receipt is null)
		{
			return null;
		}

		return await ToSnapshotAsync(context, receipt, cancellationToken);
	}

	public async Task<IReadOnlyList<TillerReceiptLine>> GetVerifiedReceiptLinesAsync(
		CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		List<ReceiptEntity> receipts = await context.Set<ReceiptEntity>()
			.AsNoTracking()
			.ToListAsync(cancellationToken);

		List<TillerReceiptLine> lines = [];
		foreach (ReceiptEntity receipt in receipts)
		{
			List<ReceiptItemEntity> items = await context.Set<ReceiptItemEntity>()
				.AsNoTracking()
				.Where(i => i.ReceiptId == receipt.Id)
				.ToListAsync(cancellationToken);

			if (items.Count == 0
				|| items.Any(i => i.NormalizationReviewState != NormalizationReviewState.Verified))
			{
				continue;
			}

			lines.AddRange(items.Select(i => new TillerReceiptLine(
				receipt.Id,
				receipt.Location,
				receipt.Date,
				i.Description,
				i.Quantity,
				i.UnitPrice,
				i.TotalAmount)));
		}

		return lines;
	}

	private static async Task<TillerReceiptSnapshot?> ToSnapshotAsync(
		ApplicationDbContext context,
		ReceiptEntity receipt,
		CancellationToken cancellationToken)
	{
		List<ReceiptItemEntity> items = await context.Set<ReceiptItemEntity>()
			.AsNoTracking()
			.Where(i => i.ReceiptId == receipt.Id)
			.ToListAsync(cancellationToken);

		if (items.Count == 0)
		{
			return null;
		}

		return new TillerReceiptSnapshot(
			receipt.Id,
			receipt.Location,
			receipt.Date,
			items.Sum(i => i.TotalAmount),
			items.All(i => i.NormalizationReviewState == NormalizationReviewState.Verified));
	}
}
