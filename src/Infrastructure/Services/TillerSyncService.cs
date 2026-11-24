using System.Security.Cryptography;
using System.Text;
using Application.Interfaces.Services;
using Application.Models.Tiller;
using Infrastructure.Entities.Core;
using Infrastructure.Tiller;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

// Native Tiller sync engine (port of the verified W32 prototype behavior
// into the application layers). Reads ONLY through the allowlisted client.
// Crash-safe: the checkpoint is saved after every page, so an interrupted
// run resumes instead of restarting, and remembered IDs make redelivery
// converge instead of duplicating.
public class TillerSyncService(
	IDbContextFactory<ApplicationDbContext> contextFactory,
	ITillerApiClient tillerApiClient,
	ITillerSelectionService selectionService,
	TillerClientOptions? options = null) : ITillerSyncService
{
	private readonly TillerClientOptions _options = options ?? new TillerClientOptions();

	public async Task<TillerSyncResult> SyncFullAsync(CancellationToken cancellationToken)
	{
		string spreadsheetId = await RequireSpreadsheetAsync(cancellationToken);
		TillerCheckpointEntity? checkpoint = await LoadCheckpointAsync(spreadsheetId, cancellationToken);
		DateOnly? fromDate = checkpoint is not null && !checkpoint.Completed
			? checkpoint.SyncedThroughDate
			: null;
		return await RunAsync(spreadsheetId, TillerSyncEventType.Full, fromDate, cancellationToken);
	}

	public async Task<TillerSyncResult> SyncIncrementalAsync(CancellationToken cancellationToken)
	{
		string spreadsheetId = await RequireSpreadsheetAsync(cancellationToken);
		TillerCheckpointEntity? checkpoint = await LoadCheckpointAsync(spreadsheetId, cancellationToken);
		return await RunAsync(spreadsheetId, TillerSyncEventType.Incremental, checkpoint?.SyncedThroughDate, cancellationToken);
	}

	public async Task<TillerSyncStatus> GetSyncStatusAsync(CancellationToken cancellationToken)
	{
		string? spreadsheetId = await selectionService.GetSelectedSpreadsheetIdAsync(cancellationToken);
		if (spreadsheetId is null)
		{
			return new TillerSyncStatus(null, null, false, null);
		}

		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		TillerCheckpointEntity? checkpoint = await context.Set<TillerCheckpointEntity>()
			.AsNoTracking()
			.SingleOrDefaultAsync(e => e.SpreadsheetId == spreadsheetId, cancellationToken);
		DateTimeOffset? lastSuccess = await context.Set<TillerSyncEventEntity>()
			.AsNoTracking()
			.Where(e => e.SpreadsheetId == spreadsheetId && e.Success)
			.OrderByDescending(e => e.OccurredAt)
			.Select(e => (DateTimeOffset?)e.OccurredAt)
			.FirstOrDefaultAsync(cancellationToken);

		return new TillerSyncStatus(
			spreadsheetId,
			checkpoint?.SyncedThroughDate,
			checkpoint?.Completed ?? false,
			lastSuccess);
	}

	private async Task<string> RequireSpreadsheetAsync(CancellationToken cancellationToken)
	{
		string? spreadsheetId = await selectionService.GetSelectedSpreadsheetIdAsync(cancellationToken);
		if (string.IsNullOrWhiteSpace(spreadsheetId))
		{
			throw new InvalidOperationException("No Tiller spreadsheet selected. Choose a readable spreadsheet first.");
		}

		return spreadsheetId;
	}

	private async Task<TillerSyncResult> RunAsync(
		string spreadsheetId,
		TillerSyncEventType eventType,
		DateOnly? fromDate,
		CancellationToken cancellationToken)
	{
		int inserted = 0, updated = 0, reconciled = 0, duplicates = 0, invalid = 0, pages = 0;
		int offset = 0;
		DateOnly? maxDate = fromDate;
		int pageSize = Math.Clamp(_options.DefaultPageSize, 1, _options.MaxPageSize);
		int? expectedTotal = null;
		HashSet<string> seenPageFingerprints = new(StringComparer.Ordinal);

		try
		{
			while (true)
			{
				TillerTransactionPage page = await tillerApiClient.QueryTransactionsAsync(
					spreadsheetId,
					new TillerTransactionQuery(fromDate, null, null, "date", "asc", pageSize, offset),
					cancellationToken);
				pages++;

				// Pagination consistency guards (verified provider contract):
				// the provider-reported total must be present and stable, pages must
				// be exact except the last, and no page may repeat (loop protection).
				if (page.TotalAvailable is not int totalAvailable)
				{
					throw new InvalidOperationException(
						"Tiller provider count missing; refusing partial sync.");
				}

				if (expectedTotal is null)
				{
					expectedTotal = totalAvailable;
				}
				else if (expectedTotal.Value != totalAvailable)
				{
					throw new InvalidOperationException(
						$"Tiller dataset changed mid-sync (was {expectedTotal.Value}, now {totalAvailable}); refusing partial sync.");
				}

				if (page.Rows.Count > pageSize)
				{
					throw new InvalidOperationException(
						$"Tiller page larger than requested ({page.Rows.Count} > {pageSize}); refusing partial sync.");
				}

				bool isLast = !page.Truncated;
				if (isLast)
				{
					if (offset + page.Rows.Count != totalAvailable)
					{
						throw new InvalidOperationException(
							$"Incomplete Tiller sync (have {offset + page.Rows.Count} of {totalAvailable}); refusing partial sync.");
					}
				}
				else if (page.Rows.Count != pageSize)
				{
					throw new InvalidOperationException(
						$"Incomplete Tiller page ({page.Rows.Count} of {pageSize} before end); refusing partial sync.");
				}

				string fingerprint = string.Join("\n", page.Rows.Select(r => r.TransactionId));
				if (!seenPageFingerprints.Add(fingerprint))
				{
					throw new InvalidOperationException(
						"Tiller repeated a page; refusing partial sync.");
				}

				foreach (TillerTransactionRow row in page.Rows)
				{
					switch (await UpsertAsync(spreadsheetId, row, cancellationToken))
					{
						case UpsertOutcome.Inserted: inserted++; break;
						case UpsertOutcome.Updated: updated++; break;
						case UpsertOutcome.Reconciled: reconciled++; break;
						case UpsertOutcome.Duplicate: duplicates++; break;
						case UpsertOutcome.Invalid: invalid++; break;
					}

					if (maxDate is null || row.Date > maxDate)
					{
						maxDate = row.Date;
					}
				}

				await SaveCheckpointAsync(spreadsheetId, maxDate, completed: !page.Truncated, cancellationToken);

				if (!page.Truncated)
				{
					break;
				}

				offset += page.Rows.Count;
				if (page.Rows.Count == 0)
				{
					break;
				}
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			await WriteEventAsync(
				spreadsheetId, eventType, success: false,
				inserted, updated, reconciled, duplicates, ex.Message, cancellationToken);
			throw;
		}

		await WriteEventAsync(
			spreadsheetId, eventType, success: true,
			inserted, updated, reconciled, duplicates, errorMessage: null, cancellationToken);
		return new TillerSyncResult(inserted, updated, reconciled, duplicates, invalid, pages);
	}

	private enum UpsertOutcome { Unchanged, Inserted, Updated, Reconciled, Duplicate, Invalid }

	private static bool IsPlaceholderId(string providerId) =>
		providerId.StartsWith("row-", StringComparison.OrdinalIgnoreCase);

	private async Task<UpsertOutcome> UpsertAsync(
		string spreadsheetId,
		TillerTransactionRow row,
		CancellationToken cancellationToken)
	{
		if (IsPlaceholderId(row.TransactionId))
		{
			// Rows without a real Transaction ID can never join safely; the
			// sheet can re-issue them under a real ID later and they would
			// otherwise import twice.
			return UpsertOutcome.Invalid;
		}

		await using ApplicationDbContext context = contextFactory.CreateDbContext();

		TillerTransactionEntity? existing = await context.Set<TillerTransactionEntity>()
			.SingleOrDefaultAsync(
				e => e.SpreadsheetId == spreadsheetId && e.ProviderTransactionId == row.TransactionId,
				cancellationToken);

		if (existing is not null)
		{
			if (FactsEqual(existing, row))
			{
				return UpsertOutcome.Unchanged;
			}

			ApplyFacts(existing, row);
			existing.Revision++;
			existing.UpdatedAt = DateTimeOffset.UtcNow;
			await context.SaveChangesAsync(cancellationToken);
			return UpsertOutcome.Updated;
		}

		bool known = await context.Set<TillerKnownIdEntity>()
			.AnyAsync(
				e => e.SpreadsheetId == spreadsheetId && e.ProviderTransactionId == row.TransactionId,
				cancellationToken);
		if (known)
		{
			return UpsertOutcome.Duplicate;
		}

		if (string.Equals(row.Status, "posted", StringComparison.OrdinalIgnoreCase))
		{
			TillerTransactionEntity? pending = await FindPendingCounterpartAsync(context, spreadsheetId, row, cancellationToken);
			if (pending is not null)
			{
				pending.SupersededBy = row.TransactionId;
				pending.UpdatedAt = DateTimeOffset.UtcNow;

				// Links and human overlays carry to the posted canonical record.
				List<TillerReceiptLinkEntity> links = await context.Set<TillerReceiptLinkEntity>()
					.Where(l => l.ProviderTransactionId == pending.ProviderTransactionId && l.SupersededAt == null)
					.ToListAsync(cancellationToken);
				foreach (TillerReceiptLinkEntity link in links)
				{
					link.ProviderTransactionId = row.TransactionId;
				}

				context.Set<TillerTransactionEntity>().Add(Canonical(spreadsheetId, row));
				context.Set<TillerKnownIdEntity>().Add(new TillerKnownIdEntity
				{
					Id = Guid.NewGuid(),
					ProviderTransactionId = row.TransactionId,
					SpreadsheetId = spreadsheetId,
					CanonicalProviderTransactionId = row.TransactionId,
					SeenAt = DateTimeOffset.UtcNow,
				});
				await context.SaveChangesAsync(cancellationToken);
				return UpsertOutcome.Reconciled;
			}
		}

		string fingerprint = Fingerprint(row);
		TillerTransactionEntity? canonical = await context.Set<TillerTransactionEntity>()
			.Where(e => e.SpreadsheetId == spreadsheetId && e.SupersededBy == null)
			.AsNoTracking()
			.ToListAsync(cancellationToken)
			.ContinueWith(t => t.Result.FirstOrDefault(e => Fingerprint(e) == fingerprint), cancellationToken);

		if (canonical is not null)
		{
			context.Set<TillerKnownIdEntity>().Add(new TillerKnownIdEntity
			{
				ProviderTransactionId = row.TransactionId,
				SpreadsheetId = spreadsheetId,
				CanonicalProviderTransactionId = canonical.ProviderTransactionId,
				SeenAt = DateTimeOffset.UtcNow,
			});
			await context.SaveChangesAsync(cancellationToken);
			return UpsertOutcome.Duplicate;
		}

		context.Set<TillerTransactionEntity>().Add(Canonical(spreadsheetId, row));
		context.Set<TillerKnownIdEntity>().Add(new TillerKnownIdEntity
		{
			ProviderTransactionId = row.TransactionId,
			SpreadsheetId = spreadsheetId,
			CanonicalProviderTransactionId = row.TransactionId,
			SeenAt = DateTimeOffset.UtcNow,
		});
		await context.SaveChangesAsync(cancellationToken);
		return UpsertOutcome.Inserted;
	}

	private static TillerTransactionEntity Canonical(string spreadsheetId, TillerTransactionRow row)
	{
		DateTimeOffset now = DateTimeOffset.UtcNow;
		TillerTransactionEntity entity = new() { Id = Guid.NewGuid() };
		ApplyFacts(entity, row);
		entity.SpreadsheetId = spreadsheetId;
		entity.ProviderTransactionId = row.TransactionId;
		entity.Revision = 1;
		entity.SupersededBy = null;
		entity.CreatedAt = now;
		entity.UpdatedAt = now;
		return entity;
	}

	private static void ApplyFacts(TillerTransactionEntity entity, TillerTransactionRow row)
	{
		entity.Date = row.Date;
		entity.Description = row.Description;
		entity.Amount = row.Amount;
		entity.Category = row.Category;
		entity.Account = row.Account;
		entity.Institution = row.Institution;
		entity.Status = row.Status;
		entity.SplitGroupId = row.SplitGroupId;
	}

	private static bool FactsEqual(TillerTransactionEntity entity, TillerTransactionRow row) =>
		entity.Date == row.Date
		&& string.Equals(entity.Description, row.Description, StringComparison.Ordinal)
		&& entity.Amount == row.Amount
		&& string.Equals(entity.Category, row.Category, StringComparison.Ordinal)
		&& string.Equals(entity.Account, row.Account, StringComparison.Ordinal)
		&& string.Equals(entity.Institution, row.Institution, StringComparison.Ordinal)
		&& string.Equals(entity.Status, row.Status, StringComparison.Ordinal)
		&& string.Equals(entity.SplitGroupId, row.SplitGroupId, StringComparison.Ordinal);

	private static async Task<TillerTransactionEntity?> FindPendingCounterpartAsync(
		ApplicationDbContext context,
		string spreadsheetId,
		TillerTransactionRow posted,
		CancellationToken cancellationToken)
	{
		List<TillerTransactionEntity> candidates = await context.Set<TillerTransactionEntity>()
			.Where(e => e.SpreadsheetId == spreadsheetId
				&& e.SupersededBy == null
				&& e.Status == "pending"
				&& e.Account == posted.Account
				&& e.Amount == posted.Amount)
			.ToListAsync(cancellationToken);

		return candidates.FirstOrDefault(e =>
			Math.Abs(e.Date.DayNumber - posted.Date.DayNumber) <= 1
			&& string.Equals(Normalize(e.Description), Normalize(posted.Description), StringComparison.Ordinal));
	}

	private static string Normalize(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}

		StringBuilder builder = new(value.Length);
		foreach (char c in value.ToLowerInvariant())
		{
			if (char.IsLetterOrDigit(c))
			{
				builder.Append(c);
			}
		}

		return builder.ToString();
	}

	private static string Fingerprint(TillerTransactionRow row) =>
		Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(
			"\0",
			row.Account,
			row.Date.ToString("yyyy-MM-dd"),
			Normalize(row.Description),
			row.Amount.ToString("0.00"),
			row.Category ?? string.Empty))));

	private static string Fingerprint(TillerTransactionEntity entity) =>
		Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(
			"\0",
			entity.Account,
			entity.Date.ToString("yyyy-MM-dd"),
			Normalize(entity.Description),
			entity.Amount.ToString("0.00"),
			entity.Category ?? string.Empty))));

	private async Task<TillerCheckpointEntity?> LoadCheckpointAsync(
		string spreadsheetId,
		CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		return await context.Set<TillerCheckpointEntity>()
			.AsNoTracking()
			.SingleOrDefaultAsync(e => e.SpreadsheetId == spreadsheetId, cancellationToken);
	}

	private async Task SaveCheckpointAsync(
		string spreadsheetId,
		DateOnly? syncedThroughDate,
		bool completed,
		CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		TillerCheckpointEntity? checkpoint = await context.Set<TillerCheckpointEntity>()
			.SingleOrDefaultAsync(e => e.SpreadsheetId == spreadsheetId, cancellationToken);

		if (checkpoint is null)
		{
			checkpoint = new TillerCheckpointEntity
			{
				Id = Guid.NewGuid(),
				SpreadsheetId = spreadsheetId,
			};
			context.Set<TillerCheckpointEntity>().Add(checkpoint);
		}

		checkpoint.SyncedThroughDate = syncedThroughDate;
		checkpoint.Completed = completed;
		checkpoint.UpdatedAt = DateTimeOffset.UtcNow;
		await context.SaveChangesAsync(cancellationToken);
	}

	private async Task WriteEventAsync(
		string spreadsheetId,
		TillerSyncEventType eventType,
		bool success,
		int inserted,
		int updated,
		int reconciled,
		int duplicates,
		string? errorMessage,
		CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		context.Set<TillerSyncEventEntity>().Add(new TillerSyncEventEntity
		{
			Id = Guid.NewGuid(),
			SpreadsheetId = spreadsheetId,
			EventType = eventType.ToString(),
			Success = success,
			Inserted = inserted,
			Updated = updated,
			Reconciled = reconciled,
			Duplicates = duplicates,
			ErrorMessage = errorMessage,
			OccurredAt = DateTimeOffset.UtcNow,
		});
		await context.SaveChangesAsync(cancellationToken);
	}
}
