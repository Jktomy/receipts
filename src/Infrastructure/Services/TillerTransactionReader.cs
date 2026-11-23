using Application.Interfaces.Services;
using Infrastructure.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

// Read-only local cache reader for the Tiller UI: paged transaction rows
// with their current link overlay, plus the append-only sync-event feed.
public class TillerTransactionReader(
	IDbContextFactory<ApplicationDbContext> contextFactory) : ITillerTransactionReader
{
	private static readonly HashSet<string> SortableColumns = new(StringComparer.OrdinalIgnoreCase)
	{
		"date", "amount", "description", "account",
	};

	public async Task<TillerTransactionListResult> GetTransactionsAsync(
		int offset,
		int limit,
		string? sortBy,
		string? sortDirection,
		string? search,
		bool unlinkedOnly,
		string? account,
		CancellationToken cancellationToken)
	{
		if (offset < 0)
		{
			throw new ArgumentException("offset must be non-negative.", nameof(offset));
		}

		if (limit <= 0 || limit > 500)
		{
			throw new ArgumentException("limit must be between 1 and 500.", nameof(limit));
		}

		if (sortBy is not null && !SortableColumns.Contains(sortBy))
		{
			throw new ArgumentException($"Invalid sortBy '{sortBy}'. Allowed: date, amount, description, account.", nameof(sortBy));
		}

		if (sortDirection is not null
			&& !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("sortDirection must be 'asc' or 'desc'.", nameof(sortDirection));
		}

		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		IQueryable<TillerTransactionEntity> query = context.Set<TillerTransactionEntity>()
			.AsNoTracking()
			.Where(e => e.SupersededBy == null);

		if (!string.IsNullOrWhiteSpace(account))
		{
			query = query.Where(e => e.Account == account);
		}

		if (!string.IsNullOrWhiteSpace(search))
		{
			query = query.Where(e => e.Description.Contains(search) || e.ProviderTransactionId.Contains(search));
		}

		List<TillerTransactionEntity> rows = await query.ToListAsync(cancellationToken);

		Dictionary<string, TillerReceiptLinkEntity> links = await context.Set<TillerReceiptLinkEntity>()
			.AsNoTracking()
			.Where(l => l.SupersededAt == null)
			.GroupBy(l => l.ProviderTransactionId)
			.Select(g => g.OrderByDescending(l => l.CreatedAt).First())
			.ToDictionaryAsync(l => l.ProviderTransactionId, cancellationToken);

		if (unlinkedOnly)
		{
			rows = rows.Where(r => !links.ContainsKey(r.ProviderTransactionId)).ToList();
		}

		bool ascending = string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);
		rows = (sortBy?.ToLowerInvariant()) switch
		{
			"amount" => (ascending ? rows.OrderBy(r => r.Amount) : rows.OrderByDescending(r => r.Amount)).ToList(),
			"description" => (ascending ? rows.OrderBy(r => r.Description) : rows.OrderByDescending(r => r.Description)).ToList(),
			"account" => (ascending ? rows.OrderBy(r => r.Account) : rows.OrderByDescending(r => r.Account)).ToList(),
			_ => (ascending ? rows.OrderBy(r => r.Date) : rows.OrderByDescending(r => r.Date)).ToList(),
		};

		int total = rows.Count;
		List<TillerStoredTransaction> page = rows
			.Skip(offset)
			.Take(limit)
			.Select(r =>
			{
				links.TryGetValue(r.ProviderTransactionId, out TillerReceiptLinkEntity? link);
				return new TillerStoredTransaction(
					r.Id, r.ProviderTransactionId, r.Date, r.Description, r.Amount,
					r.Category, r.Account, r.Institution, r.Status, r.SplitGroupId,
					r.Revision, link?.LinkKind, link?.ReceiptId);
			})
			.ToList();

		return new TillerTransactionListResult(page, total);
	}

	public async Task<TillerSyncEventListResult> GetSyncEventsAsync(
		int offset,
		int limit,
		bool? success,
		CancellationToken cancellationToken)
	{
		if (offset < 0)
		{
			throw new ArgumentException("offset must be non-negative.", nameof(offset));
		}

		if (limit <= 0 || limit > 500)
		{
			throw new ArgumentException("limit must be between 1 and 500.", nameof(limit));
		}

		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		IQueryable<TillerSyncEventEntity> query = context.Set<TillerSyncEventEntity>()
			.AsNoTracking()
			.OrderByDescending(e => e.OccurredAt);

		if (success.HasValue)
		{
			query = query.Where(e => e.Success == success.Value);
		}

		int total = await query.CountAsync(cancellationToken);
		List<TillerStoredSyncEvent> rows = await query
			.Skip(offset)
			.Take(limit)
			.Select(e => new TillerStoredSyncEvent(
				e.Id, e.EventType, e.Success, e.Inserted, e.Updated,
				e.Reconciled, e.Duplicates, e.ErrorMessage, e.OccurredAt))
			.ToListAsync(cancellationToken);

		return new TillerSyncEventListResult(rows, total);
	}
}
