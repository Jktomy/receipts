using System.ComponentModel;
using API.Mapping.Core;
using API.Tiller;
using Application.Commands.Receipt.CreateComplete;
using Application.Commands.Receipt.Update;
using Application.Commands.Tiller;
using Application.Interfaces.Services;
using Application.Models;
using Application.Models.Tiller;
using Application.Queries.Core.Receipt;
using Application.Queries.Core.ReceiptItem;
using Application.Queries.Core.Tiller;
using Domain;
using Domain.Core;
using Mediator;
using ModelContextProtocol.Server;
using ReceiptResponse = API.Generated.Dtos.ReceiptResponse;

namespace API.Mcp;

// Fixed typed MCP face over the existing Tracer CQRS contract.
// Same app services, validators, provenance rules, and human-approval guards
// as the HTTP layer. No keymgmt/admin/delete/purge surface by construction:
// only these eight tools are registered.
public sealed record McpReceiptItemInput(
	string Description,
	decimal Quantity,
	decimal UnitPrice,
	string? Category = null);

[McpServerToolType]
public sealed class TracerMcpTools(
	IMediator mediator,
	ReceiptMapper receiptMapper,
	TillerMapper tillerMapper)
{
	[McpServerTool(Name = "receipts_list", Title = "List receipts")]
	[Description("List receipts, newest first by default. Read-only.")]
	public async Task<PagedResult<ReceiptListItem>> ReceiptsListAsync(
		[Description("Max rows (1-100).")] int limit = 25,
		[Description("Zero-based offset.")] int offset = 0,
		[Description("Free-text filter.")] string? query = null,
		[Description("Store/location filter.")] string? location = null,
		CancellationToken cancellationToken = default)
	{
		limit = Math.Clamp(limit, 1, 100);
		offset = Math.Max(offset, 0);
		return await mediator.Send(
			new GetAllReceiptsQuery(offset, limit, SortParams.Default, null, null, query, location),
			cancellationToken);
	}

	[McpServerTool(Name = "receipts_get", Title = "Get receipt")]
	[Description("Get one receipt header by id. Read-only. Throws when absent.")]
	public async Task<Receipt> ReceiptsGetAsync(
		[Description("Receipt id.")] Guid id,
		CancellationToken cancellationToken = default)
	{
		Receipt? receipt = await mediator.Send(new GetReceiptByIdQuery(id), cancellationToken);
		return receipt ?? throw new InvalidOperationException($"Receipt {id} not found.");
	}

	[McpServerTool(Name = "receipt_items_by_receipt", Title = "List receipt items")]
	[Description("List line items for one receipt. Read-only.")]
	public async Task<PagedResult<ReceiptItem>> ReceiptItemsByReceiptAsync(
		[Description("Receipt id.")] Guid receiptId,
		[Description("Max rows (1-200).")] int limit = 50,
		[Description("Zero-based offset.")] int offset = 0,
		CancellationToken cancellationToken = default)
	{
		limit = Math.Clamp(limit, 1, 200);
		offset = Math.Max(offset, 0);
		return await mediator.Send(
			new GetReceiptItemsByReceiptIdQuery(receiptId, offset, limit, SortParams.Default),
			cancellationToken);
	}

	[McpServerTool(Name = "receipts_create", Title = "Create receipt draft")]
	[Description("Create a receipt draft with line items. New items enter the human-review normalization flow; categories may be left empty. SourceKind is never asserted by the caller.")]
	public async Task<ReceiptResponse> ReceiptsCreateAsync(
		[Description("Store/location.")] string location,
		[Description("Receipt date YYYY-MM-DD.")] string date,
		[Description("Line items.")] IReadOnlyList<McpReceiptItemInput> items,
		[Description("Tax amount, 0 when unknown.")] decimal taxAmount = 0m,
		[Description("True when tax is unknown (placeholder 0).")] bool taxUnknown = false,
		[Description("Optional idempotency key: same key on retry returns the already-created receipt instead of double-creating. Omit only for one-shot creates.")] string? clientRequestId = null,
		CancellationToken cancellationToken = default)
	{
		Receipt receipt = new(
			Guid.NewGuid(),
			location,
			ParseReceiptDate(date),
			new Money(taxAmount),
			sourceKind: null,
			taxUnknown: taxUnknown);
		List<ReceiptItem> domainItems = items.Select(i => new ReceiptItem(
			Guid.NewGuid(),
			receiptItemCode: null,
			i.Description,
			i.Quantity,
			new Money(i.UnitPrice),
			new Money(i.Quantity * i.UnitPrice),
			i.Category ?? string.Empty,
			subcategory: null)).ToList();
		CreateCompleteReceiptResult result = await mediator.Send(
			new CreateCompleteReceiptCommand(receipt, [], domainItems, [], ParseClientRequestId(clientRequestId)),
			cancellationToken);
		return receiptMapper.ToResponse(result.Receipt);
	}

	private static DateOnly ParseReceiptDate(string date)
	{
		if (!DateOnly.TryParse(date, out DateOnly parsed))
		{
			throw new ArgumentException(
				$"Invalid receipt date '{date}'. Expected format YYYY-MM-DD.", nameof(date));
		}

		return parsed;
	}

	private static Guid? ParseClientRequestId(string? clientRequestId)
	{
		if (string.IsNullOrWhiteSpace(clientRequestId))
		{
			// Omit only for one-shot creates: without a key, a retried call
			// creates a second receipt instead of returning the first.
			return null;
		}

		if (!Guid.TryParse(clientRequestId, out Guid parsed))
		{
			throw new ArgumentException(
				$"Invalid clientRequestId '{clientRequestId}'. Expected a UUID string.", nameof(clientRequestId));
		}

		return parsed;
	}

	[McpServerTool(Name = "receipts_correct", Title = "Correct receipt")]
	[Description("Correct location, tax and/or provenance on a receipt. Only supplied fields change; items are preserved.")]
	public async Task<bool> ReceiptsCorrectAsync(
		[Description("Receipt id.")] Guid id,
		[Description("Corrected store/location, or null to keep.")] string? location = null,
		[Description("Corrected tax amount, or null to keep.")] decimal? taxAmount = null,
		[Description("Corrected tax-unknown flag, or null to keep.")] bool? taxUnknown = null,
		[Description("Provenance kind (photo-observed, user-reported, paperless-verified), or null to keep. paperless-verified requires sourcePaperlessDocumentId.")] string? sourceKind = null,
		[Description("Paperless-ngx document id this receipt was imported from, or null to keep. Binds once; a different id is rejected.")] long? sourcePaperlessDocumentId = null,
		CancellationToken cancellationToken = default)
	{
		if (sourceKind is not null && !Receipt.AdmittedSourceKinds.Contains(sourceKind))
		{
			throw new ArgumentException(Receipt.SourceKindNotAdmitted, nameof(sourceKind));
		}

		if (sourcePaperlessDocumentId is not null && (sourcePaperlessDocumentId <= 0 || sourcePaperlessDocumentId > 2147483647))
		{
			throw new ArgumentException(Receipt.SourcePaperlessDocumentIdOutOfRange, nameof(sourcePaperlessDocumentId));
		}

		Receipt? current = await mediator.Send(new GetReceiptByIdQuery(id), cancellationToken);
		if (current is null)
		{
			throw new InvalidOperationException($"Receipt {id} not found.");
		}

		Receipt updated = new(
			current.Id,
			location ?? current.Location,
			current.Date,
			taxAmount.HasValue ? new Money(taxAmount.Value) : current.TaxAmount,
			sourceKind: sourceKind ?? current.SourceKind,
			taxUnknown: taxUnknown ?? current.TaxUnknown)
		{
			PaidFromEnvelopeId = current.PaidFromEnvelopeId,
			OccasionId = current.OccasionId,
			SourcePaperlessDocumentId = sourcePaperlessDocumentId ?? current.SourcePaperlessDocumentId
		};
		return await mediator.Send(new UpdateReceiptCommand([updated]), cancellationToken);
	}

	[McpServerTool(Name = "tiller_transactions", Title = "Query Tiller transactions")]
	[Description("Query synced Tiller transactions (local reads only, no source writes).")]
	public async Task<TillerTransactionListDto> TillerTransactionsAsync(
		[Description("Max rows (1-100).")] int limit = 25,
		[Description("Zero-based offset.")] int offset = 0,
		[Description("Only unlinked rows.")] bool unlinkedOnly = false,
		[Description("Account display-name filter.")] string? account = null,
		CancellationToken cancellationToken = default)
	{
		limit = Math.Clamp(limit, 1, 100);
		offset = Math.Max(offset, 0);
		TillerTransactionListResult result = await mediator.Send(
			new GetTillerTransactionsQuery(offset, limit, null, null, null, unlinkedOnly, account),
			cancellationToken);
		return tillerMapper.ToTransactionListDto(result);
	}

	[McpServerTool(Name = "tiller_suggest", Title = "Suggest receipt match")]
	[Description("Deterministic receipt-match suggestion for a transaction. Local reads only.")]
	public async Task<TillerMatchSuggestionDto> TillerSuggestAsync(
		[Description("Provider transaction id.")] string providerTransactionId,
		CancellationToken cancellationToken = default)
	{
		TillerMatchSuggestion suggestion = await mediator.Send(
			new GetTillerMatchSuggestionQuery(providerTransactionId), cancellationToken);
		return tillerMapper.ToSuggestionDto(suggestion);
	}

	[McpServerTool(Name = "tiller_confirm", Title = "Confirm receipt link")]
	[Description("Confirm a receipt link for a transaction (human-approval equivalent). Records the MCP channel as reviewer.")]
	public async Task<TillerMatchSuggestionDto> TillerConfirmAsync(
		[Description("Provider transaction id.")] string providerTransactionId,
		[Description("Receipt id to link.")] Guid receiptId,
		CancellationToken cancellationToken = default)
	{
		TillerMatchSuggestion suggestion = await mediator.Send(
			new ConfirmTillerLinkCommand(providerTransactionId, receiptId, "mcp"),
			cancellationToken);
		return tillerMapper.ToSuggestionDto(suggestion);
	}
}
