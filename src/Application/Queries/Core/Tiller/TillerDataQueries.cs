using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Models.Tiller;
namespace Application.Queries.Core.Tiller;

public record GetTillerTransactionsQuery(
	int Offset,
	int Limit,
	string? SortBy,
	string? SortDirection,
	string? Search,
	bool UnlinkedOnly,
	string? Account) : IQuery<TillerTransactionListResult>;

public class GetTillerTransactionsQueryHandler(
	ITillerTransactionReader reader) : Mediator.IRequestHandler<GetTillerTransactionsQuery, TillerTransactionListResult>
{
	public async ValueTask<TillerTransactionListResult> Handle(
		GetTillerTransactionsQuery request,
		CancellationToken cancellationToken)
	{
		return await reader.GetTransactionsAsync(
			request.Offset,
			request.Limit,
			request.SortBy,
			request.SortDirection,
			request.Search,
			request.UnlinkedOnly,
			request.Account,
			cancellationToken);
	}
}

public record GetTillerSyncStatusQuery : IQuery<TillerSyncStatus>;

public class GetTillerSyncStatusQueryHandler(
	ITillerSyncService syncService) : Mediator.IRequestHandler<GetTillerSyncStatusQuery, TillerSyncStatus>
{
	public async ValueTask<TillerSyncStatus> Handle(
		GetTillerSyncStatusQuery request,
		CancellationToken cancellationToken)
	{
		return await syncService.GetSyncStatusAsync(cancellationToken);
	}
}

public record GetTillerSyncEventsQuery(
	int Offset,
	int Limit,
	bool? Success) : IQuery<TillerSyncEventListResult>;

public class GetTillerSyncEventsQueryHandler(
	ITillerTransactionReader reader) : Mediator.IRequestHandler<GetTillerSyncEventsQuery, TillerSyncEventListResult>
{
	public async ValueTask<TillerSyncEventListResult> Handle(
		GetTillerSyncEventsQuery request,
		CancellationToken cancellationToken)
	{
		return await reader.GetSyncEventsAsync(request.Offset, request.Limit, request.Success, cancellationToken);
	}
}

public record GetTillerAccountMappingsQuery(string? SpreadsheetId) : IQuery<List<TillerAccountMappingDto>>;

public class GetTillerAccountMappingsQueryHandler(
	ITillerAccountMappingService mappingService,
	ITillerSelectionService selectionService) : Mediator.IRequestHandler<GetTillerAccountMappingsQuery, List<TillerAccountMappingDto>>
{
	public async ValueTask<List<TillerAccountMappingDto>> Handle(
		GetTillerAccountMappingsQuery request,
		CancellationToken cancellationToken)
	{
		string? spreadsheetId = request.SpreadsheetId
			?? await selectionService.GetSelectedSpreadsheetIdAsync(cancellationToken);
		if (string.IsNullOrWhiteSpace(spreadsheetId))
		{
			return [];
		}

		return await mappingService.GetBySpreadsheetIdAsync(spreadsheetId, cancellationToken);
	}
}
