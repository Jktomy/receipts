using Application.Interfaces.Services;
using Application.Models.Tiller;
using Infrastructure.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

// Deterministic receipt<->transaction matcher (port of the verified W32
// matcher behavior into the application layers). Purchase receipts carry
// positive totals and match money-out (negative) rows cent-for-cent;
// money-in rows (refunds) never match a purchase receipt. Receipt facts
// come from the read-only receipt catalog; only all-verified receipts can
// match. Ambiguity yields neutral candidates for explicit human
// confirmation — never an auto-link.
public class TillerMatchingService(
	IDbContextFactory<ApplicationDbContext> contextFactory,
	ITillerReceiptCatalog receiptCatalog) : ITillerMatchingService
{
	private const int WindowDays = 3;

	// A settled charge above a verified receipt base by a modest delta is a
	// POSSIBLE tip — a suggestion for HUMAN confirmation only. The delta is
	// capped so a genuinely different purchase never reads as a gratuity.
	private const decimal MaxTipRatio = 0.25m;

	public async Task<TillerMatchSuggestion> SuggestAsync(
		string providerTransactionId,
		CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		TillerTransactionEntity? txn = await ResolveAsync(context, providerTransactionId, cancellationToken);
		if (txn is null)
		{
			return new TillerMatchSuggestion(TillerMatchDecision.Unknown, null, null, []);
		}

		TillerReceiptLinkEntity? manual = await ActiveLinkAsync(context, txn.ProviderTransactionId, "manual", cancellationToken);
		if (manual is not null)
		{
			return new TillerMatchSuggestion(TillerMatchDecision.Manual, manual.ReceiptId, null, []);
		}

		TillerReceiptLinkEntity? auto = await ActiveLinkAsync(context, txn.ProviderTransactionId, "auto", cancellationToken);
		if (auto is not null)
		{
			return await WithConfidenceAsync(
				new TillerMatchSuggestion(TillerMatchDecision.Linked, auto.ReceiptId, null, []),
				txn,
				auto.ReceiptId,
				cancellationToken);
		}

		List<TillerMatchCandidate> pairs = await MatchingReceiptsAsync(context, txn, cancellationToken);
		if (pairs.Count == 0)
		{
			return await SuggestPossibleTipAsync(context, txn, cancellationToken);
		}

		if (pairs.Count == 1)
		{
			TillerMatchCandidate pair = pairs[0];
			List<TillerMatchCandidate> contenders = await MatchingTxnsAsync(context, pair.ReceiptId, cancellationToken);
			if (contenders.Count > 1)
			{
				return new TillerMatchSuggestion(TillerMatchDecision.Ambiguous, null, null, contenders);
			}

			await AddLinkAsync(context, txn.ProviderTransactionId, pair.ReceiptId, "auto", reviewer: null, cancellationToken);
			await context.SaveChangesAsync(cancellationToken);
			return await WithConfidenceAsync(
				new TillerMatchSuggestion(TillerMatchDecision.Linked, pair.ReceiptId, null, []),
				txn,
				pair.ReceiptId,
				cancellationToken);
		}

		return new TillerMatchSuggestion(TillerMatchDecision.Ambiguous, null, null, pairs);
	}

	public async Task<TillerMatchSuggestion> SuggestForReceiptAsync(
		string providerTransactionId,
		Guid receiptId,
		CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		TillerTransactionEntity? txn = await ResolveAsync(context, providerTransactionId, cancellationToken);
		if (txn is null)
		{
			return new TillerMatchSuggestion(TillerMatchDecision.Unknown, null, null, []);
		}

		TillerReceiptLinkEntity? manual = await ActiveLinkAsync(context, txn.ProviderTransactionId, "manual", cancellationToken);
		if (manual is not null && manual.ReceiptId == receiptId)
		{
			return new TillerMatchSuggestion(TillerMatchDecision.Manual, receiptId, null, []);
		}

		TillerReceiptLinkEntity? auto = await ActiveLinkAsync(context, txn.ProviderTransactionId, "auto", cancellationToken);
		if (auto is not null && auto.ReceiptId == receiptId)
		{
			return await WithConfidenceAsync(
				new TillerMatchSuggestion(TillerMatchDecision.Linked, receiptId, null, []),
				txn,
				receiptId,
				cancellationToken);
		}

		TillerReceiptSnapshot? receipt = await receiptCatalog.GetReceiptSnapshotAsync(receiptId, cancellationToken);
		if (receipt is null || MatchEvidence(receipt, txn) is null)
		{
			// No exact total: a settled charge above a verified, tax-known,
			// tip-free receipt base is a POSSIBLE tip for HUMAN confirmation.
			// Never auto-links; a row already linked elsewhere stays silent.
			if (receipt is not null
				&& !await HasActiveLinkAsync(context, txn.ProviderTransactionId, cancellationToken)
				&& TryPossibleTip(receipt, txn, out decimal possibleTip))
			{
				return await WithConfidenceAsync(
					new TillerMatchSuggestion(TillerMatchDecision.PossibleTip, receiptId, null, [], possibleTip),
					txn,
					receiptId,
					cancellationToken);
			}

			return new TillerMatchSuggestion(TillerMatchDecision.None, null, null, []);
		}

		return new TillerMatchSuggestion(TillerMatchDecision.Taken, null, null, []);
	}

	public async Task<TillerMatchSuggestion> ConfirmAsync(
		string providerTransactionId,
		Guid receiptId,
		string? reviewer,
		CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		TillerTransactionEntity? txn = await ResolveAsync(context, providerTransactionId, cancellationToken);
		if (txn is null)
		{
			throw new InvalidOperationException($"Unknown transaction '{providerTransactionId}'.");
		}

		TillerReceiptSnapshot? receipt = await receiptCatalog.GetReceiptSnapshotAsync(receiptId, cancellationToken);
		if (receipt is null || !receipt.AllItemsVerified)
		{
			throw new InvalidOperationException("Manual confirm requires a human-verified receipt (every line item Verified).");
		}

		TillerReceiptLinkEntity? holder = await context.Set<TillerReceiptLinkEntity>()
			.SingleOrDefaultAsync(
				l => l.ReceiptId == receiptId
					&& l.ProviderTransactionId != txn.ProviderTransactionId
					&& l.SupersededAt == null,
				cancellationToken);
		if (holder is not null)
		{
			throw new InvalidOperationException("That receipt is already linked to a different transaction.");
		}

		List<TillerReceiptLinkEntity> existing = await context.Set<TillerReceiptLinkEntity>()
			.Where(l => l.ProviderTransactionId == txn.ProviderTransactionId && l.SupersededAt == null)
			.ToListAsync(cancellationToken);
		context.Set<TillerReceiptLinkEntity>().RemoveRange(existing);

		await AddLinkAsync(context, txn.ProviderTransactionId, receiptId, "manual", reviewer, cancellationToken);
		await context.SaveChangesAsync(cancellationToken);
		return new TillerMatchSuggestion(TillerMatchDecision.Manual, receiptId, null, []);
	}

	public async Task<TillerMatchSuggestion> UnlinkAsync(
		string providerTransactionId,
		string? reviewer,
		CancellationToken cancellationToken)
	{
		TillerTransactionEntity? canonical;
		await using (ApplicationDbContext context = contextFactory.CreateDbContext())
		{
			canonical = await ResolveAsync(context, providerTransactionId, cancellationToken);
			if (canonical is null)
			{
				return new TillerMatchSuggestion(TillerMatchDecision.Unknown, null, null, []);
			}

			List<TillerReceiptLinkEntity> existing = await context.Set<TillerReceiptLinkEntity>()
				.Where(l => l.ProviderTransactionId == canonical.ProviderTransactionId && l.SupersededAt == null)
				.ToListAsync(cancellationToken);
			context.Set<TillerReceiptLinkEntity>().RemoveRange(existing);

			context.Set<TillerLinkEventEntity>().Add(new TillerLinkEventEntity
			{
				Id = Guid.NewGuid(),
				ProviderTransactionId = canonical.ProviderTransactionId,
				Action = "unlink",
				ReceiptId = null,
				Reviewer = reviewer,
				At = DateTimeOffset.UtcNow,
			});
			await context.SaveChangesAsync(cancellationToken);
		}

		return await SuggestAsync(canonical.ProviderTransactionId, cancellationToken);
	}

	public async Task<IReadOnlyList<TillerLinkHistoryEntry>> GetHistoryAsync(
		string providerTransactionId,
		CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		return await context.Set<TillerLinkEventEntity>()
			.AsNoTracking()
			.Where(e => e.ProviderTransactionId == providerTransactionId)
			.OrderBy(e => e.At)
			.Select(e => new TillerLinkHistoryEntry(e.Action, e.ReceiptId, e.Reviewer, e.At))
			.ToListAsync(cancellationToken);
	}

	public async Task<TillerPaymentContext?> GetPaymentContextAsync(
		Guid receiptId,
		CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		TillerReceiptLinkEntity? link = await context.Set<TillerReceiptLinkEntity>()
			.AsNoTracking()
			.Where(l => l.ReceiptId == receiptId && l.SupersededAt == null)
			.OrderByDescending(l => l.CreatedAt)
			.FirstOrDefaultAsync(cancellationToken);

		if (link is null)
		{
			return null;
		}

		TillerTransactionEntity? txn = await context.Set<TillerTransactionEntity>()
			.AsNoTracking()
			.SingleOrDefaultAsync(
				e => e.ProviderTransactionId == link.ProviderTransactionId && e.SupersededBy == null,
				cancellationToken);

		if (txn is null)
		{
			return null;
		}

		return new TillerPaymentContext(
			txn.ProviderTransactionId,
			txn.Date,
			txn.Description,
			txn.Amount,
			txn.Account,
			txn.Category,
			link.LinkKind);
	}

	private static async Task<TillerTransactionEntity?> ResolveAsync(
		ApplicationDbContext context,
		string providerTransactionId,
		CancellationToken cancellationToken)
	{
		TillerTransactionEntity? txn = await context.Set<TillerTransactionEntity>()
			.SingleOrDefaultAsync(
				e => e.ProviderTransactionId == providerTransactionId,
				cancellationToken);

		while (txn?.SupersededBy is not null)
		{
			string next = txn.SupersededBy;
			txn = await context.Set<TillerTransactionEntity>()
				.SingleOrDefaultAsync(
					e => e.ProviderTransactionId == next,
					cancellationToken);
		}

		return txn;
	}

	private static async Task<TillerReceiptLinkEntity?> ActiveLinkAsync(
		ApplicationDbContext context,
		string providerTransactionId,
		string kind,
		CancellationToken cancellationToken) =>
		await context.Set<TillerReceiptLinkEntity>()
			.SingleOrDefaultAsync(
				l => l.ProviderTransactionId == providerTransactionId
					&& l.LinkKind == kind
					&& l.SupersededAt == null,
				cancellationToken);

	private static async Task AddLinkAsync(
		ApplicationDbContext context,
		string providerTransactionId,
		Guid receiptId,
		string kind,
		string? reviewer,
		CancellationToken cancellationToken)
	{
		context.Set<TillerReceiptLinkEntity>().Add(new TillerReceiptLinkEntity
		{
			Id = Guid.NewGuid(),
			ReceiptId = receiptId,
			ProviderTransactionId = providerTransactionId,
			LinkKind = kind,
			Reviewer = reviewer,
			CreatedAt = DateTimeOffset.UtcNow,
		});
		context.Set<TillerLinkEventEntity>().Add(new TillerLinkEventEntity
		{
			Id = Guid.NewGuid(),
			ProviderTransactionId = providerTransactionId,
			Action = kind == "manual" ? "confirm" : "auto-link",
			ReceiptId = receiptId,
			Reviewer = reviewer,
			At = DateTimeOffset.UtcNow,
		});
		await Task.CompletedTask;
	}

	private async Task<TillerMatchSuggestion> WithConfidenceAsync(
		TillerMatchSuggestion suggestion,
		TillerTransactionEntity txn,
		Guid receiptId,
		CancellationToken cancellationToken)
	{
		// Confidence is recomputed from the stored dates so links never carry
		// stale labels after corrections.
		TillerReceiptSnapshot? receipt = await receiptCatalog.GetReceiptSnapshotAsync(receiptId, cancellationToken);
		int days = receipt is null ? 0 : Math.Abs(receipt.Date.DayNumber - txn.Date.DayNumber);
		return suggestion with { Confidence = days == 0 ? "exact" : "close" };
	}

	private async Task<List<TillerMatchCandidate>> MatchingReceiptsAsync(
		ApplicationDbContext context,
		TillerTransactionEntity txn,
		CancellationToken cancellationToken)
	{
		IReadOnlyList<TillerReceiptSnapshot> receipts =
			await receiptCatalog.GetReceiptSnapshotsAsync(cancellationToken);

		List<TillerMatchCandidate> matches = [];
		foreach (TillerReceiptSnapshot receipt in receipts)
		{
			if (!receipt.AllItemsVerified)
			{
				continue;
			}

			if (await IsReceiptTakenAsync(context, receipt.ReceiptId, txn.ProviderTransactionId, cancellationToken))
			{
				continue;
			}

			int? days = MatchEvidence(receipt, txn);
			if (days.HasValue)
			{
				matches.Add(new TillerMatchCandidate(receipt.ReceiptId, txn.ProviderTransactionId, days.Value));
			}
		}

		return matches;
	}

	private async Task<List<TillerMatchCandidate>> MatchingTxnsAsync(
		ApplicationDbContext context,
		Guid receiptId,
		CancellationToken cancellationToken)
	{
		TillerReceiptSnapshot? receipt = await receiptCatalog.GetReceiptSnapshotAsync(receiptId, cancellationToken);
		if (receipt is null || !receipt.AllItemsVerified)
		{
			return [];
		}

		List<TillerTransactionEntity> txns = await context.Set<TillerTransactionEntity>()
			.AsNoTracking()
			.Where(e => e.SupersededBy == null)
			.ToListAsync(cancellationToken);

		List<TillerMatchCandidate> matches = [];
		foreach (TillerTransactionEntity txn in txns)
		{
			if (await HasActiveLinkAsync(context, txn.ProviderTransactionId, cancellationToken))
			{
				continue;
			}

			int? days = MatchEvidence(receipt, txn);
			if (days.HasValue)
			{
				matches.Add(new TillerMatchCandidate(receiptId, txn.ProviderTransactionId, days.Value));
			}
		}

		return matches;
	}

	// Returns the date distance in days when the receipt matches the row.
	private static int? MatchEvidence(TillerReceiptSnapshot receipt, TillerTransactionEntity txn)
	{
		if (txn.Amount >= 0)
		{
			// Money-in rows (refunds) never match a purchase receipt.
			return null;
		}

		if (receipt.Total != -txn.Amount)
		{
			return null;
		}

		return MatchWindow(receipt.Merchant, receipt.Date, txn);
	}

	// Merchant + date window only (no amount comparison): shared by exact
	// matches and tip hypotheses so both use identical identity evidence.
	private static int? MatchWindow(string merchant, DateOnly date, TillerTransactionEntity txn)
	{
		if (!MerchantMatches(merchant, txn.Description))
		{
			return null;
		}

		int days = Math.Abs(date.DayNumber - txn.Date.DayNumber);
		return days <= WindowDays ? days : null;
	}

	// A possible tip: verified receipt base (items + known tax, no tip yet)
	// below a settled posted charge by a modest delta. Suggestion ONLY —
	// callers must never auto-link from this; human confirmation required.
	private static bool TryPossibleTip(TillerReceiptSnapshot receipt, TillerTransactionEntity txn, out decimal tip)
	{
		tip = 0m;
		if (!receipt.AllItemsVerified || receipt.TaxUnknown || receipt.TipTotal != 0m)
		{
			return false;
		}

		if (!string.Equals(txn.Status, "posted", StringComparison.OrdinalIgnoreCase) || txn.Amount >= 0)
		{
			return false;
		}

		decimal @base = receipt.Total + receipt.TaxTotal;
		if (@base <= 0m)
		{
			return false;
		}

		decimal delta = -txn.Amount - @base;
		if (delta <= 0m || delta > @base * MaxTipRatio)
		{
			return false;
		}

		if (!MatchWindow(receipt.Merchant, receipt.Date, txn).HasValue)
		{
			return false;
		}

		tip = delta;
		return true;
	}

	private async Task<TillerMatchSuggestion> SuggestPossibleTipAsync(
		ApplicationDbContext context,
		TillerTransactionEntity txn,
		CancellationToken cancellationToken)
	{
		IReadOnlyList<TillerReceiptSnapshot> receipts =
			await receiptCatalog.GetReceiptSnapshotsAsync(cancellationToken);

		List<(Guid ReceiptId, int Days, decimal Tip)> tips = [];
		foreach (TillerReceiptSnapshot receipt in receipts)
		{
			if (!receipt.AllItemsVerified)
			{
				continue;
			}

			if (await IsReceiptTakenAsync(context, receipt.ReceiptId, txn.ProviderTransactionId, cancellationToken))
			{
				continue;
			}

			if (TryPossibleTip(receipt, txn, out decimal tip)
				&& MatchWindow(receipt.Merchant, receipt.Date, txn) is int days)
			{
				tips.Add((receipt.ReceiptId, days, tip));
			}
		}

		if (tips.Count == 0)
		{
			return new TillerMatchSuggestion(TillerMatchDecision.None, null, null, []);
		}

		if (tips.Count == 1)
		{
			List<TillerMatchCandidate> contenders =
			[
				new TillerMatchCandidate(tips[0].ReceiptId, txn.ProviderTransactionId, tips[0].Days),
			];
			contenders.AddRange(await MatchingTxnsAsync(context, tips[0].ReceiptId, cancellationToken));
			contenders.AddRange(await OtherTipContendersAsync(context, tips[0].ReceiptId, txn.ProviderTransactionId, cancellationToken));
			if (contenders.Count == 1)
			{
				return await WithConfidenceAsync(
					new TillerMatchSuggestion(TillerMatchDecision.PossibleTip, tips[0].ReceiptId, null, [], tips[0].Tip),
					txn,
					tips[0].ReceiptId,
					cancellationToken);
			}

			return new TillerMatchSuggestion(TillerMatchDecision.Ambiguous, null, null, contenders);
		}

		return new TillerMatchSuggestion(
			TillerMatchDecision.Ambiguous,
			null,
			null,
			[.. tips.Select(t => new TillerMatchCandidate(t.ReceiptId, txn.ProviderTransactionId, t.Days))]);
	}

	// Other settled charges that read as a tip on the same receipt contest
	// the suggestion. Rows already linked elsewhere cannot contest: they
	// have a receipt.
	private async Task<List<TillerMatchCandidate>> OtherTipContendersAsync(
		ApplicationDbContext context,
		Guid receiptId,
		string exceptTxn,
		CancellationToken cancellationToken)
	{
		TillerReceiptSnapshot? receipt = await receiptCatalog.GetReceiptSnapshotAsync(receiptId, cancellationToken);
		if (receipt is null)
		{
			return [];
		}

		List<TillerTransactionEntity> txns = await context.Set<TillerTransactionEntity>()
			.AsNoTracking()
			.Where(e => e.SupersededBy == null && e.ProviderTransactionId != exceptTxn)
			.ToListAsync(cancellationToken);

		List<TillerMatchCandidate> contenders = [];
		foreach (TillerTransactionEntity other in txns)
		{
			if (!TryPossibleTip(receipt, other, out _))
			{
				continue;
			}

			if (await HasActiveLinkAsync(context, other.ProviderTransactionId, cancellationToken))
			{
				continue;
			}

			if (MatchWindow(receipt.Merchant, receipt.Date, other) is int days)
			{
				contenders.Add(new TillerMatchCandidate(receiptId, other.ProviderTransactionId, days));
			}
		}

		return contenders;
	}

	private static async Task<bool> IsReceiptTakenAsync(
		ApplicationDbContext context,
		Guid receiptId,
		string exceptTxn,
		CancellationToken cancellationToken) =>
		await context.Set<TillerReceiptLinkEntity>()
			.AnyAsync(
				l => l.ReceiptId == receiptId
					&& l.ProviderTransactionId != exceptTxn
					&& l.SupersededAt == null,
				cancellationToken);

	private static async Task<bool> HasActiveLinkAsync(
		ApplicationDbContext context,
		string providerTransactionId,
		CancellationToken cancellationToken) =>
		await context.Set<TillerReceiptLinkEntity>()
			.AnyAsync(
				l => l.ProviderTransactionId == providerTransactionId && l.SupersededAt == null,
				cancellationToken);

	private static bool MerchantMatches(string? receiptMerchant, string? txnDescription)
	{
		List<string> receiptTokens = Tokens(receiptMerchant);
		if (receiptTokens.Count == 0)
		{
			return false;
		}

		HashSet<string> txnTokens = [.. Tokens(txnDescription)];
		return receiptTokens.All(txnTokens.Contains);
	}

	private static List<string> Tokens(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return [];
		}

		List<string> tokens = [];
		System.Text.StringBuilder current = new();
		foreach (char c in value.ToLowerInvariant())
		{
			if (char.IsLetterOrDigit(c))
			{
				current.Append(c);
			}
			else if (current.Length > 0)
			{
				tokens.Add(current.ToString());
				current.Clear();
			}
		}

		if (current.Length > 0)
		{
			tokens.Add(current.ToString());
		}

		return tokens;
	}
}
