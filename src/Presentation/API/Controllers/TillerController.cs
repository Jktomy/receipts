using API.Authentication;
using API.Filters;
using API.Http;
using API.Tiller;
using Application.Commands.Tiller;
using Application.Interfaces.Services;
using Application.Models.Tiller;
using Application.Queries.Core.Tiller;
using Asp.Versioning;
using Common;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiVersion("1.0")]
[ApiController]
[Route("api/tiller")]
[Authorize]
public class TillerController(
	IMediator mediator,
	TillerMapper mapper,
	ILogger<TillerController> logger) : ControllerBase
{
	private static readonly HashSet<string> TransactionSortColumns = new(StringComparer.OrdinalIgnoreCase)
	{
		"date", "amount", "description", "account",
	};

	[HttpGet("connection-status")]
	[EndpointSummary("Get Tiller connection status")]
	[EndpointDescription("Returns whether the read-only Tiller interface is configured, whether it is reachable, the selected spreadsheet, and the last successful sync timestamp. Performs no upstream writes.")]
	[RequireKeyScope(ApiKeyScopes.TillerRead)]
	public async Task<Ok<TillerConnectionStatusDto>> GetConnectionStatus(CancellationToken cancellationToken)
	{
		TillerConnectionStatus status = await mediator.Send(new GetTillerConnectionStatusQuery(), cancellationToken);
		return TypedResults.Ok(mapper.ToConnectionStatusDto(status));
	}

	[HttpGet("status")]
	[EndpointSummary("Get Tiller sync status")]
	[EndpointDescription("Returns the local sync checkpoint and last successful sync timestamp. Derived from local state; performs no live Tiller call.")]
	[RequireKeyScope(ApiKeyScopes.TillerRead)]
	public async Task<Ok<TillerSyncStatusDto>> GetStatus(CancellationToken cancellationToken)
	{
		TillerSyncStatus status = await mediator.Send(new GetTillerSyncStatusQuery(), cancellationToken);
		return TypedResults.Ok(mapper.ToSyncStatusDto(status));
	}

	[HttpGet("spreadsheets")]
	[EndpointSummary("List readable Tiller spreadsheets")]
	[EndpointDescription("Returns the spreadsheets available through the connected read-only Tiller interface.")]
	[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
	[RequireTillerConfigured(allowExecutorCache: true)]
	[RequireKeyScope(ApiKeyScopes.TillerRead)]
	public async Task<Results<Ok<IReadOnlyList<TillerSpreadsheetDto>>, StatusCodeHttpResult>> GetSpreadsheets(
		CancellationToken cancellationToken)
	{
		try
		{
			IReadOnlyList<TillerSpreadsheet> sheets = await mediator.Send(new GetTillerSpreadsheetsQuery(), cancellationToken);
			return TypedResults.Ok(mapper.ToSpreadsheetDtos(sheets));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "Tiller spreadsheet list failed");
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}
	}

	[HttpGet("settings/spreadsheet")]
	[EndpointSummary("Get the selected Tiller spreadsheet")]
	[EndpointDescription("Returns the currently selected readable spreadsheet ID, if any.")]
	[RequireKeyScope(ApiKeyScopes.TillerRead)]
	public async Task<Ok<TillerSelectedSpreadsheetDto>> GetSpreadsheetSettings(CancellationToken cancellationToken)
	{
		string? selected = await mediator.Send(new GetSelectedTillerSpreadsheetQuery(), cancellationToken);
		return TypedResults.Ok(new TillerSelectedSpreadsheetDto(selected));
	}

	[HttpPut("settings/spreadsheet")]
	[EndpointSummary("Select the Tiller spreadsheet")]
	[EndpointDescription("Validates the spreadsheet against the live readable list, then stores it as the only spreadsheet Tiller reads may touch.")]
	[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
	[RequireTillerConfigured]
	[RequireKeyScope(ApiKeyScopes.TillerConfirm)]
	public async Task<Results<Ok<TillerSelectedSpreadsheetDto>, BadRequestProblem, StatusCodeHttpResult>> SelectSpreadsheet(
		[FromBody] SelectTillerSpreadsheetRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			await mediator.Send(new SelectTillerSpreadsheetCommand(request.SpreadsheetId), cancellationToken);
			return TypedResults.Ok(new TillerSelectedSpreadsheetDto(request.SpreadsheetId));
		}
		catch (ArgumentException ex)
		{
			return ApiProblem.BadRequest(ex.Message);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "Tiller spreadsheet selection failed");
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}
	}

	[HttpGet("accounts")]
	[EndpointSummary("List Tiller accounts")]
	[EndpointDescription("Returns the financial accounts connected to Tiller. Read-only; no refresh is ever triggered.")]
	[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
	[RequireTillerConfigured(allowExecutorCache: true)]
	[RequireKeyScope(ApiKeyScopes.TillerRead)]
	public async Task<Results<Ok<IReadOnlyList<TillerAccountDto>>, StatusCodeHttpResult>> GetAccounts(
		CancellationToken cancellationToken)
	{
		try
		{
			IReadOnlyList<TillerAccount> accounts = await mediator.Send(new GetTillerAccountsQuery(), cancellationToken);
			return TypedResults.Ok(mapper.ToAccountDtos(accounts));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "Tiller account list failed");
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}
	}

	[HttpGet("categories")]
	[EndpointSummary("List Tiller categories")]
	[EndpointDescription("Returns the budget categories of the selected spreadsheet. Empty when no spreadsheet is selected.")]
	[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
	[RequireTillerConfigured]
	[RequireKeyScope(ApiKeyScopes.TillerRead)]
	public async Task<Results<Ok<IReadOnlyList<TillerCategoryDto>>, StatusCodeHttpResult>> GetCategories(
		CancellationToken cancellationToken)
	{
		try
		{
			IReadOnlyList<TillerCategory> categories = await mediator.Send(new GetTillerCategoriesQuery(), cancellationToken);
			return TypedResults.Ok(mapper.ToCategoryDtos(categories));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "Tiller category list failed");
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}
	}

	[HttpPost("sync")]
	[EndpointSummary("Run a Tiller sync")]
	[EndpointDescription("Backfills history or incrementally syncs new rows into the local cache through the read-only interface. Runs inline in this request; creates no scheduler, worker, or upstream writes.")]
	[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
	[RequireTillerConfigured]
	[RequireKeyScope(ApiKeyScopes.TillerConfirm)]
	public async Task<Results<Ok<TillerSyncResultDto>, BadRequestProblem, StatusCodeHttpResult>> Sync(
		[FromBody] SyncTillerRequest request,
		CancellationToken cancellationToken)
	{
		if (!string.Equals(request.Mode, "full", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(request.Mode, "incremental", StringComparison.OrdinalIgnoreCase))
		{
			return ApiProblem.BadRequest("mode must be 'full' or 'incremental'");
		}

		try
		{
			TillerSyncResult result = await mediator.Send(new SyncTillerCommand(request.Mode), cancellationToken);
			return TypedResults.Ok(mapper.ToSyncResultDto(result));
		}
		catch (InvalidOperationException ex)
		{
			return ApiProblem.BadRequest(ex.Message);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "Tiller sync failed");
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}
	}

	// Executor supplies the complete read-only Treasury scan. Disabled unless
	// the deployment explicitly binds the one approved source spreadsheet.
	[HttpPost("executor-snapshot")]
	[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
	[RequireKeyScope(ApiKeyScopes.TillerConfirm)]
	[RequestSizeLimit(20000000)]
	public async Task<Results<Ok<TillerSyncResultDto>, BadRequestProblem, StatusCodeHttpResult>> ImportExecutorSnapshot(
		[FromBody] ExecutorSnapshotRequest request,
		[FromServices] ITillerSyncService sync,
		[FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration,
		CancellationToken cancellationToken)
	{
		string? allowed = configuration["TILLER_EXECUTOR_SPREADSHEET_ID"];
		if (string.IsNullOrWhiteSpace(allowed))
		{
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}
		if (!string.Equals(allowed, request.SpreadsheetId, StringComparison.Ordinal) || request.Rows is null || request.Rows.Any(r => r is null))
		{
			return ApiProblem.BadRequest("Snapshot source is not the approved spreadsheet or contains invalid rows.");
		}
		try
		{
			var rows = request.Rows.Select(r => new TillerTransactionRow(r.TransactionId, r.Date, r.Description,
				r.Amount, r.Category, r.Account, r.Institution, r.Status, r.SplitGroupId)).ToList();
			TillerSyncResult result = request.Metadata is null
				? await sync.ImportExecutorSnapshotAsync(allowed, request.Total, rows, cancellationToken)
				: await sync.ImportExecutorSnapshotAsync(allowed, request.Total, rows, request.Metadata, cancellationToken);
			return TypedResults.Ok(mapper.ToSyncResultDto(result));
		}
		catch (InvalidOperationException)
		{
			return ApiProblem.BadRequest("Snapshot rejected; check source count, IDs, and sync status.");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning("Executor snapshot import failed.");
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}
	}

	public sealed record ExecutorSnapshotRequest(
		[property: System.Text.Json.Serialization.JsonRequired] string SpreadsheetId,
		[property: System.Text.Json.Serialization.JsonRequired] int Total,
		[property: System.Text.Json.Serialization.JsonRequired] List<ExecutorSnapshotRow> Rows,
		TillerExecutorMetadata? Metadata = null);

	public sealed record ExecutorSnapshotRow(
		[property: System.Text.Json.Serialization.JsonRequired] string TransactionId,
		[property: System.Text.Json.Serialization.JsonRequired] DateOnly Date,
		[property: System.Text.Json.Serialization.JsonRequired] string Description,
		[property: System.Text.Json.Serialization.JsonRequired] decimal Amount,
		string? Category,
		[property: System.Text.Json.Serialization.JsonRequired] string Account,
		[property: System.Text.Json.Serialization.JsonRequired] string Institution,
		[property: System.Text.Json.Serialization.JsonRequired] string Status,
		string? SplitGroupId);

	[HttpGet("transactions")]
	[EndpointSummary("List cached Tiller transactions")]
	[EndpointDescription("Paged, filterable feed of locally cached Tiller rows with their current receipt-link overlay. Reads local cache only; performs no live Tiller call.")]
	[RequireKeyScope(ApiKeyScopes.TillerRead)]
	public async Task<Results<Ok<TillerTransactionListDto>, BadRequestProblem>> GetTransactions(
		[FromQuery] int offset = 0,
		[FromQuery] int limit = 50,
		[FromQuery] string? sortBy = null,
		[FromQuery] string? sortDirection = null,
		[FromQuery] string? search = null,
		[FromQuery] bool unlinkedOnly = false,
		[FromQuery] string? account = null,
		CancellationToken cancellationToken = default)
	{
		if (offset < 0)
		{
			return ApiProblem.BadRequest("offset must be non-negative");
		}

		if (limit <= 0 || limit > 500)
		{
			return ApiProblem.BadRequest("limit must be between 1 and 500");
		}

		if (sortBy is not null && !TransactionSortColumns.Contains(sortBy))
		{
			return ApiProblem.BadRequest("sortBy must be one of: date, amount, description, account");
		}

		if (sortDirection is not null
			&& !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase))
		{
			return ApiProblem.BadRequest("sortDirection must be 'asc' or 'desc'");
		}

		TillerTransactionListResult result = await mediator.Send(
			new GetTillerTransactionsQuery(offset, limit, sortBy, sortDirection, search, unlinkedOnly, account),
			cancellationToken);
		return TypedResults.Ok(mapper.ToTransactionListDto(result));
	}

	[HttpGet("account-mappings")]
	[EndpointSummary("List Tiller account mappings")]
	[EndpointDescription("Returns local-account to Tiller-account mappings for the selected spreadsheet (or the requested one).")]
	[RequireKeyScope(ApiKeyScopes.TillerRead)]
	public async Task<Ok<TillerAccountMappingListDto>> GetAccountMappings(
		[FromQuery] string? spreadsheetId,
		CancellationToken cancellationToken)
	{
		List<Application.Models.Tiller.TillerAccountMappingDto> mappings = await mediator.Send(
			new GetTillerAccountMappingsQuery(spreadsheetId), cancellationToken);
		return TypedResults.Ok(mapper.ToMappingListDto(mappings));
	}

	[HttpPost("account-mappings")]
	[EndpointSummary("Create a Tiller account mapping")]
	[EndpointDescription("Maps one local account to one Tiller account within a single spreadsheet.")]
	[RequireKeyScope(ApiKeyScopes.TillerConfirm)]
	public async Task<Results<Ok<TillerAccountMappingResponse>, BadRequestProblem>> CreateAccountMapping(
		[FromBody] CreateTillerAccountMappingRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.TillerAccountId) || string.IsNullOrWhiteSpace(request.SpreadsheetId))
		{
			return ApiProblem.BadRequest("tillerAccountId and spreadsheetId are required");
		}

		Application.Models.Tiller.TillerAccountMappingDto created = await mediator.Send(
			new CreateTillerAccountMappingCommand(
				request.ReceiptsAccountId, request.TillerAccountId, request.TillerAccountName ?? string.Empty, request.SpreadsheetId),
			cancellationToken);
		return TypedResults.Ok(mapper.ToMappingDto(created));
	}

	[HttpPut("account-mappings/{id:guid}")]
	[EndpointSummary("Update a Tiller account mapping")]
	[EndpointDescription("Changes the Tiller side of a mapping. The spreadsheet binding can never change; delete and recreate to move spreadsheets.")]
	[RequireKeyScope(ApiKeyScopes.TillerConfirm)]
	public async Task<Results<NoContent, BadRequestProblem>> UpdateAccountMapping(
		[FromRoute] Guid id,
		[FromBody] UpdateTillerAccountMappingRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			await mediator.Send(
				new UpdateTillerAccountMappingCommand(
					id, request.TillerAccountId, request.TillerAccountName ?? string.Empty, request.SpreadsheetId),
				cancellationToken);
			return TypedResults.NoContent();
		}
		catch (ArgumentException ex)
		{
			return ApiProblem.BadRequest(ex.Message);
		}
	}

	[HttpDelete("account-mappings/{id:guid}")]
	[EndpointSummary("Delete a Tiller account mapping")]
	[EndpointDescription("Removes a mapping. Synced rows and links are preserved; only the mapping is deleted.")]
	[RequireKeyScope(ApiKeyScopes.TillerConfirm)]
	public async Task<NoContent> DeleteAccountMapping(
		[FromRoute] Guid id,
		CancellationToken cancellationToken)
	{
		await mediator.Send(new DeleteTillerAccountMappingCommand(id), cancellationToken);
		return TypedResults.NoContent();
	}

	[HttpGet("transactions/{providerTransactionId}/suggestion")]
	[EndpointSummary("Get the receipt-match suggestion for a transaction")]
	[EndpointDescription("Deterministic suggestion: exact single links lock (auto), ambiguity lists neutral candidates, missing receipts report none. Local reads only.")]
	[RequireKeyScope(ApiKeyScopes.TillerRead)]
	public async Task<Results<Ok<TillerMatchSuggestionDto>, NotFound>> GetSuggestion(
		[FromRoute] string providerTransactionId,
		CancellationToken cancellationToken)
	{
		TillerMatchSuggestion suggestion = await mediator.Send(
			new GetTillerMatchSuggestionQuery(providerTransactionId), cancellationToken);
		if (suggestion.Decision == TillerMatchDecision.Unknown)
		{
			return TypedResults.NotFound();
		}

		return TypedResults.Ok(mapper.ToSuggestionDto(suggestion));
	}

	[HttpGet("transactions/{providerTransactionId}/history")]
	[EndpointSummary("Get link history for a transaction")]
	[EndpointDescription("Reviewer audit trail for confirm/unlink/auto-link actions. Local reads only.")]
	[RequireKeyScope(ApiKeyScopes.TillerRead)]
	public async Task<Ok<TillerLinkHistoryListDto>> GetHistory(
		[FromRoute] string providerTransactionId,
		CancellationToken cancellationToken)
	{
		IReadOnlyList<TillerLinkHistoryEntry> history = await mediator.Send(
			new GetTillerLinkHistoryQuery(providerTransactionId), cancellationToken);
		return TypedResults.Ok(mapper.ToHistoryListDto(history));
	}

	[HttpPost("transactions/{providerTransactionId}/confirm")]
	[EndpointSummary("Manually confirm a receipt link")]
	[EndpointDescription("Records an explicit human confirm. Requires a human-verified receipt; rejects already-linked receipts.")]
	[RequireKeyScope(ApiKeyScopes.TillerConfirm)]
	public async Task<Results<Ok<TillerMatchSuggestionDto>, BadRequestProblem, NotFound>> ConfirmLink(
		[FromRoute] string providerTransactionId,
		[FromBody] ConfirmTillerLinkRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			TillerMatchSuggestion suggestion = await mediator.Send(
				new ConfirmTillerLinkCommand(providerTransactionId, request.ReceiptId, request.Reviewer),
				cancellationToken);
			return TypedResults.Ok(mapper.ToSuggestionDto(suggestion));
		}
		catch (InvalidOperationException ex) when (ex.Message.StartsWith("Unknown transaction", StringComparison.Ordinal))
		{
			return TypedResults.NotFound();
		}
		catch (InvalidOperationException ex)
		{
			return ApiProblem.BadRequest(ex.Message);
		}
	}

	[HttpPost("transactions/{providerTransactionId}/unlink")]
	[EndpointSummary("Remove the receipt link from a transaction")]
	[EndpointDescription("Deletes active links (history is preserved) and returns a fresh suggestion.")]
	[RequireKeyScope(ApiKeyScopes.TillerConfirm)]
	public async Task<Results<Ok<TillerMatchSuggestionDto>, NotFound>> Unlink(
		[FromRoute] string providerTransactionId,
		[FromBody] UnlinkTillerLinkRequest? request,
		CancellationToken cancellationToken)
	{
		TillerMatchSuggestion suggestion = await mediator.Send(
			new UnlinkTillerLinkCommand(providerTransactionId, request?.Reviewer),
			cancellationToken);
		if (suggestion.Decision == TillerMatchDecision.Unknown)
		{
			return TypedResults.NotFound();
		}

		return TypedResults.Ok(mapper.ToSuggestionDto(suggestion));
	}

	[HttpGet("receipts/{receiptId:guid}/payment-context")]
	[EndpointSummary("Get payment context for a receipt")]
	[EndpointDescription("Returns the linked Tiller row facts (account, date, category) for a receipt, if linked. Local reads only.")]
	[RequireKeyScope(ApiKeyScopes.TillerRead)]
	public async Task<Results<Ok<TillerPaymentContextDto>, NotFound>> GetPaymentContext(
		[FromRoute] Guid receiptId,
		CancellationToken cancellationToken)
	{
		TillerPaymentContext? context = await mediator.Send(
			new GetTillerPaymentContextQuery(receiptId), cancellationToken);
		TillerPaymentContextDto? dto = mapper.ToPaymentContextDto(context);
		if (dto is null)
		{
			return TypedResults.NotFound();
		}

		return TypedResults.Ok(dto);
	}

	[HttpGet("events")]
	[EndpointSummary("Get recent Tiller sync events")]
	[EndpointDescription("Paginated, filterable feed of local sync attempts, most recent first. Local reads only.")]
	[RequireKeyScope(ApiKeyScopes.TillerRead)]
	public async Task<Results<Ok<TillerSyncEventListDto>, BadRequestProblem>> GetEvents(
		[FromQuery] int offset = 0,
		[FromQuery] int limit = 50,
		[FromQuery] string? outcome = null,
		CancellationToken cancellationToken = default)
	{
		if (offset < 0)
		{
			return ApiProblem.BadRequest("offset must be non-negative");
		}

		if (limit <= 0 || limit > 500)
		{
			return ApiProblem.BadRequest("limit must be between 1 and 500");
		}

		bool? success;
		switch (outcome?.ToLowerInvariant())
		{
			case null or "":
				success = null;
				break;
			case "success":
				success = true;
				break;
			case "failure":
				success = false;
				break;
			default:
				return ApiProblem.BadRequest("outcome must be 'success' or 'failure'");
		}

		TillerSyncEventListResult result = await mediator.Send(
			new GetTillerSyncEventsQuery(offset, limit, success), cancellationToken);
		return TypedResults.Ok(mapper.ToSyncEventListDto(result));
	}

	[HttpGet("analytics")]
	[EndpointSummary("Get Tiller purchase-intelligence analytics")]
	[EndpointDescription("Product facts from verified receipts only; unmatched bank rows as neutral context; bank and captured totals in separate buckets. Local reads only.")]
	[RequireKeyScope(ApiKeyScopes.TillerRead)]
	public async Task<Ok<TillerAnalyticsDto>> GetAnalytics(CancellationToken cancellationToken)
	{
		TillerAnalyticsResult result = await mediator.Send(new GetTillerAnalyticsQuery(), cancellationToken);
		return TypedResults.Ok(mapper.ToAnalyticsDto(result));
	}
}
