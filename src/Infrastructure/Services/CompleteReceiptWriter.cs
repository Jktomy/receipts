using Application.Commands.Receipt.CreateComplete;
using Application.Interfaces.Services;
using Application.Normalization;
using Domain.Core;
using Infrastructure.Entities.Core;
using Infrastructure.Mapping;
using Infrastructure.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Infrastructure.Services;

public class CompleteReceiptWriter(
	IDbContextFactory<ApplicationDbContext> contextFactory,
	ReceiptMapper receiptMapper,
	TransactionMapper transactionMapper,
	ReceiptItemMapper receiptItemMapper,
	AdjustmentMapper adjustmentMapper,
		Microsoft.Extensions.Options.IOptions<Application.Taxonomy.ClassificationOptions>? classificationOptions = null) : ICompleteReceiptWriter
{
	private const string SourceIndexName = "IX_Receipts_SourcePaperlessDocumentId";
	private const int ReplayLookupAttempts = 3;
	private static readonly TimeSpan ReplayLookupDelay = TimeSpan.FromMilliseconds(100);
	public Task<CreateCompleteReceiptResult> CreateAsync(Receipt receipt, List<Transaction> transactions, List<ReceiptItem> items,
		List<Adjustment> adjustments, CancellationToken cancellationToken)
		=> CreateCoreAsync(receipt, transactions, items, adjustments, null, cancellationToken);

	public Task<CreateCompleteReceiptResult> CreateAsync(Receipt receipt, List<Transaction> transactions, List<ReceiptItem> items,
		List<Adjustment> adjustments, Guid clientRequestId, CancellationToken cancellationToken)
		=> CreateCoreAsync(receipt, transactions, items, adjustments, clientRequestId, cancellationToken);

	private async Task<CreateCompleteReceiptResult> CreateCoreAsync(
		Receipt receipt,
		List<Transaction> transactions,
		List<ReceiptItem> items,
		List<Adjustment> adjustments,
		Guid? clientRequestId, CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
		await using IDbContextTransaction? transaction = context.Database.IsRelational() ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
		await NormalizationWriteGuard.AcquireWriteGateAsync(context, cancellationToken);
		ClassificationService.RequireRequestId(items.Select(receiptItemMapper.ToEntity), clientRequestId);
		ReceiptWriteRequestEntity? request = null;
		if (clientRequestId is Guid requestId)
		{
			request = await ReceiptWriteIdempotency.FindOrCreateAsync(context, requestId, "complete", new
			{
				receipt.Location,
				receipt.Date,
				TaxAmount = receipt.TaxAmount.Amount,
				TaxCurrency = receipt.TaxAmount.Currency,
				TaxUnknown = receipt.TaxUnknown,
				SourceKind = receipt.SourceKind,
				receipt.PaidFromEnvelopeId,
				receipt.OccasionId,
				receipt.SourcePaperlessDocumentId,
				transactions = transactions.Select(value => new { value.CardId, TransactionAmount = value.Amount.Amount, TransactionCurrency = value.Amount.Currency, value.Date }),
				items = items.Select(value => new { value.Description, value.ReceiptItemCode, value.Quantity, value.UnitOfMeasure, value.PackageContentQuantity, value.PackageContentUnit, UnitAmount = value.UnitPrice.Amount, UnitCurrency = value.UnitPrice.Currency, TotalAmount = value.TotalAmount.Amount, TotalCurrency = value.TotalAmount.Currency, value.NormalizationIntent, value.Classification, value.EnvelopeOverrideId, value.OccasionId }),
				adjustments = adjustments.Select(value => new { value.Type, AdjustmentAmount = value.Amount.Amount, AdjustmentCurrency = value.Amount.Currency, value.Description })
			}, cancellationToken);
			if (request.ReceiptId != Guid.Empty)
			{
				ReceiptEntity? stored = await context.Receipts.IgnoreQueryFilters().SingleOrDefaultAsync(value => value.Id == request.ReceiptId, cancellationToken);
				if (stored is null || stored.DeletedAt is not null) { throw new NormalizationCatalogException(409, "receipt_request_deleted", "This request already created a receipt that is now deleted. Restore it instead of replaying creation."); }
				var savedItems = await context.ReceiptItems.Where(value => value.ReceiptId == stored.Id).ToListAsync(cancellationToken);
				var savedTransactions = await context.Transactions.Where(value => value.ReceiptId == stored.Id).ToListAsync(cancellationToken);
				var savedAdjustments = await context.Adjustments.Where(value => value.ReceiptId == stored.Id).ToListAsync(cancellationToken);
				await CurrentClassificationReader.PopulateAsync(context, savedItems, cancellationToken);
				return new(receiptMapper.ToDomain(stored), savedTransactions.Select(transactionMapper.ToDomain).ToList(), savedItems.Select(receiptItemMapper.ToDomain).ToList(), savedAdjustments.Select(adjustmentMapper.ToDomain).ToList());
			}
		}

		// Map receipt and pre-generate ID for FK assignment
		ReceiptEntity receiptEntity = receiptMapper.ToEntity(receipt);
		receiptEntity.Id = Guid.NewGuid();

		// Transactions store only the originating card; its parent determines the account.
		List<TransactionEntity> transactionEntities = transactions.Select(t =>
		{
			TransactionEntity entity = transactionMapper.ToEntity(t);
			entity.Id = Guid.NewGuid();
			entity.ReceiptId = receiptEntity.Id;
			entity.CardId = t.CardId;
			return entity;
		}).ToList();

		// Map receipt items, assigning FK
		List<ReceiptItemEntity> itemEntities = items.Select(i =>
		{
			ReceiptItemEntity entity = receiptItemMapper.ToEntity(i);
			entity.Id = Guid.NewGuid();
			entity.ReceiptId = receiptEntity.Id;
			return entity;
		}).ToList();

		List<AdjustmentEntity> adjustmentEntities = adjustments.Select(adjustment =>
		{
			AdjustmentEntity entity = adjustmentMapper.ToEntity(adjustment);
			entity.Id = Guid.NewGuid();
			entity.ReceiptId = receiptEntity.Id;
			return entity;
		}).ToList();

		if (request is not null)
		{
			request.ReceiptId = receiptEntity.Id;
			request.ItemIds = itemEntities.Select(item => item.Id).ToArray();
		}

		await ItemEnvelopeWriter.RequireChoicesAsync(context, [receiptEntity.PaidFromEnvelopeId], [receiptEntity.OccasionId], cancellationToken);
		await ItemEnvelopeWriter.PrepareNewItemsAsync(context, itemEntities, receiptEntity, cancellationToken);

		// Add all to context and persist in a single SaveChangesAsync (implicit transaction)
		context.Set<ReceiptEntity>().Add(receiptEntity);
		context.Set<TransactionEntity>().AddRange(transactionEntities);
		context.Set<ReceiptItemEntity>().AddRange(itemEntities);
		context.Set<AdjustmentEntity>().AddRange(adjustmentEntities);

		await ClassificationService.PrepareAggregateAsync(context, itemEntities, classificationOptions?.Value.WritesEnabled == true, cancellationToken);
		// Load required response state before committing, so a failed read cannot leave
		// a saved transaction behind a failed create response. Tracking fixes up the cards.
		List<Guid> cardIds = transactionEntities.Select(entity => entity.CardId).Distinct().ToList();
		await context.Cards.IgnoreAutoIncludes().Where(card => cardIds.Contains(card.Id)).LoadAsync(cancellationToken);
		try
		{
			await context.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateException ex) when (ex.InnerException is PostgresException
		{ SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: SourceIndexName }
			&& receipt.SourcePaperlessDocumentId is long sourceId)
		{
			// A replayed Paperless import raced or retried past the request-level
			// idempotency gate. Roll back the poisoned transaction, then resolve to
			// the winning receipt in a fresh context so the replay is idempotent.
			if (transaction is not null) { await transaction.RollbackAsync(cancellationToken); }
			return await LoadReplayedReceiptAsync(sourceId, clientRequestId, cancellationToken);
		}
		await ClassificationService.ApplyAggregateAsync(context, itemEntities, classificationOptions?.Value.WritesEnabled == true, cancellationToken);
		await CategoryAssistanceObservationWriter.RecordAggregateAsync(context, itemEntities, clientRequestId, cancellationToken);
		await CurrentClassificationReader.PopulateAsync(context, itemEntities, cancellationToken);
		if (transaction is not null) { await transaction.CommitAsync(cancellationToken); }


		// Map back to domain
		Receipt createdReceipt = receiptMapper.ToDomain(receiptEntity);
		List<Transaction> createdTransactions = transactionEntities.Select(transactionMapper.ToDomain).ToList();
		List<ReceiptItem> createdItems = itemEntities.Select(receiptItemMapper.ToDomain).ToList();
		List<Adjustment> createdAdjustments = adjustmentEntities.Select(adjustmentMapper.ToDomain).ToList();

		return new CreateCompleteReceiptResult(createdReceipt, createdTransactions, createdItems, createdAdjustments);
	}

	private async Task<CreateCompleteReceiptResult> LoadReplayedReceiptAsync(
		long sourcePaperlessDocumentId, Guid? clientRequestId, CancellationToken cancellationToken)
	{
		// Fresh context: the write transaction was rolled back after the unique
		// violation, so the original context cannot query. A racing winner may not
		// be visible yet under READ COMMITTED; retry briefly, then fail loudly.
		for (int attempt = 1; attempt <= ReplayLookupAttempts; attempt++)
		{
			await using ApplicationDbContext lookup = await contextFactory.CreateDbContextAsync(cancellationToken);
			ReceiptEntity? stored = await lookup.Receipts.IgnoreQueryFilters()
				.SingleOrDefaultAsync(value => value.SourcePaperlessDocumentId == sourcePaperlessDocumentId, cancellationToken);
			if (stored is not null)
			{
				if (stored.DeletedAt is not null) { throw new NormalizationCatalogException(409, "receipt_source_deleted", "This Paperless document already created a receipt that is now deleted. Restore it instead of replaying creation."); }
				var savedItems = await lookup.ReceiptItems.Where(value => value.ReceiptId == stored.Id).ToListAsync(cancellationToken);
				var savedTransactions = await lookup.Transactions.Where(value => value.ReceiptId == stored.Id).ToListAsync(cancellationToken);
				var savedAdjustments = await lookup.Adjustments.Where(value => value.ReceiptId == stored.Id).ToListAsync(cancellationToken);
				await CurrentClassificationReader.PopulateAsync(lookup, savedItems, cancellationToken);
				return new(receiptMapper.ToDomain(stored), savedTransactions.Select(transactionMapper.ToDomain).ToList(), savedItems.Select(receiptItemMapper.ToDomain).ToList(), savedAdjustments.Select(adjustmentMapper.ToDomain).ToList());
			}
			if (attempt < ReplayLookupAttempts) { await Task.Delay(ReplayLookupDelay, cancellationToken); }
		}
		throw new NormalizationCatalogException(409, "receipt_replay_unresolved", "A receipt for this Paperless document was created concurrently but is not yet visible. Retry the import.");
	}
}
