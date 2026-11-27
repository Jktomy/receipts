using Application.Duplicates;
using Application.Interfaces.Services;
using Application.Models.Reports;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

public class DuplicateReportReader(IDbContextFactory<ApplicationDbContext> contextFactory) : IDuplicateReportReader
{
	public async Task<DuplicateDetectionResult> GetDuplicatesAsync(
		string matchOn,
		string locationTolerance,
		decimal totalTolerance,
		bool includeAccepted,
		CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

		List<DuplicateReceiptSummary> receipts = await Summaries(context, context.Receipts.AsNoTracking()
			.Where(r => r.DeletedAt == null)).ToListAsync(cancellationToken);

		List<DuplicateGroup> groups = DuplicateDetectionPolicy.Group(receipts, matchOn, locationTolerance, totalTolerance);

		// Suppression is applied AFTER clustering, on receipt identities, so it is unaffected by the
		// tolerance / normalization settings that shaped the clusters above (RECEIPTS-834).
		//
		// Scoped to the receipts that actually landed in a group: an unpredicated read of the whole
		// table made report cost scale with every acceptance in the database, and paid that cost even
		// when clustering produced nothing. Only pairs whose BOTH ends are in a group can suppress
		// one, so anything else is dead weight.
		HashSet<Guid> clusteredReceiptIds = [.. groups.SelectMany(g => g.Receipts).Select(r => r.ReceiptId)];
		HashSet<(Guid A, Guid B)> acceptedPairs = clusteredReceiptIds.Count == 0
			? []
			: await LoadAcceptedPairsAsync(context, clusteredReceiptIds, cancellationToken);

		List<DuplicateGroup> visibleGroups = [];
		foreach (DuplicateGroup group in groups)
		{
			bool isAccepted = DuplicateAcceptancePolicy.IsFullyAccepted(group.Receipts.Select(r => r.ReceiptId), acceptedPairs);
			if (isAccepted && !includeAccepted)
			{
				continue;
			}

			visibleGroups.Add(group with { IsAccepted = isAccepted });
		}

		int totalDuplicateReceipts = visibleGroups.Sum(g => g.Receipts.Count);
		return new DuplicateDetectionResult(visibleGroups, visibleGroups.Count, totalDuplicateReceipts);
	}

	public async Task<AcceptedDuplicatesResult> GetAcceptedDuplicatesAsync(CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

		var pairRows = await context.AcceptedDuplicatePairs
			.AsNoTracking()
			.Select(p => new { p.ReceiptIdA, p.ReceiptIdB, p.AcceptedAt })
			.ToListAsync(cancellationToken);

		List<DuplicateAcceptedPair> pairs = [.. pairRows
			.Select(p => new DuplicateAcceptedPair(p.ReceiptIdA, p.ReceiptIdB, p.AcceptedAt))];

		if (pairs.Count == 0)
		{
			return new AcceptedDuplicatesResult([], 0);
		}

		List<DuplicateAcceptanceComponent> components = DuplicateAcceptancePolicy.Components(pairs);
		HashSet<Guid> allMembers = [.. components.SelectMany(c => c.ReceiptIds)];

		Dictionary<Guid, DuplicateReceiptSummary> summaries = await Summaries(context, context.Receipts.AsNoTracking()
			.Where(r => r.DeletedAt == null && allMembers.Contains(r.Id)))
			.ToDictionaryAsync(s => s.ReceiptId, cancellationToken);

		return DuplicateAcceptancePolicy.ProjectGroups(components, summaries);
	}

	private static IQueryable<DuplicateReceiptSummary> Summaries(ApplicationDbContext context, IQueryable<Infrastructure.Entities.Core.ReceiptEntity> receipts) =>
		from r in receipts
		let transactionTotal = context.Transactions
			.Where(t => t.ReceiptId == r.Id && t.DeletedAt == null)
			.Sum(t => (decimal?)t.Amount) ?? 0m
		let hasPayment = context.Transactions.Any(t => t.ReceiptId == r.Id && t.DeletedAt == null)
		let hasItems = context.ReceiptItems.Any(i => i.ReceiptId == r.Id && i.DeletedAt == null)
		let itemTotal = context.ReceiptItems.Where(i => i.ReceiptId == r.Id && i.DeletedAt == null)
			.Sum(i => (decimal?)i.TotalAmount) ?? 0m
		let adjustmentTotal = context.Adjustments.Where(a => a.ReceiptId == r.Id && a.DeletedAt == null)
			.Sum(a => (decimal?)a.Amount) ?? 0m
		select new DuplicateReceiptSummary(r.Id, r.Location, r.Date, transactionTotal)
		{
			ComparisonTotal = hasPayment ? transactionTotal :
				(hasItems && !r.TaxUnknown ? itemTotal + r.TaxAmount + adjustmentTotal : (decimal?)null)
		};

	internal static async Task<int> CountUnacceptedDefaultGroupsAsync(ApplicationDbContext context, CancellationToken cancellationToken)
	{
		// Only same-date/same-location rows with an actual comparison total can be exact duplicates.
		// First restrict to date/location collisions in SQL, then use the same stable comparison and
		// pairwise suppression policy as the report. Do not load unrelated receipts or receipt items.
		var receipts = context.Receipts.AsNoTracking().Where(r => r.DeletedAt == null);
		var colliding = receipts.Where(r => receipts.Any(other =>
			other.Id != r.Id && other.Date == r.Date && other.Location == r.Location));
		List<DuplicateReceiptSummary> candidates = await Summaries(context, colliding).ToListAsync(cancellationToken);
		List<DuplicateGroup> groups = DuplicateDetectionPolicy.Group(candidates, "dateAndLocationAndTotal", "exact", 0m);
		HashSet<Guid> memberIds = [.. groups.SelectMany(g => g.Receipts).Select(r => r.ReceiptId)];
		if (memberIds.Count == 0)
		{
			return 0;
		}
		HashSet<(Guid A, Guid B)> accepted = await LoadAcceptedPairsAsync(context, memberIds, cancellationToken);
		return groups.Count(g => !DuplicateAcceptancePolicy.IsFullyAccepted(g.Receipts.Select(r => r.ReceiptId), accepted));
	}

	private static async Task<HashSet<(Guid A, Guid B)>> LoadAcceptedPairsAsync(
		ApplicationDbContext context,
		HashSet<Guid> receiptIds,
		CancellationToken cancellationToken)
	{
		var rows = await context.AcceptedDuplicatePairs
			.AsNoTracking()
			.Where(p => receiptIds.Contains(p.ReceiptIdA) && receiptIds.Contains(p.ReceiptIdB))
			.Select(p => new { p.ReceiptIdA, p.ReceiptIdB })
			.ToListAsync(cancellationToken);

		return [.. rows.Select(r => (r.ReceiptIdA, r.ReceiptIdB))];
	}

}
