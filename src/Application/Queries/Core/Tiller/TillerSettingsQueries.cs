using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Models.Tiller;
namespace Application.Queries.Core.Tiller;

public record GetTillerConnectionStatusQuery : IQuery<TillerConnectionStatus>;

public class GetTillerConnectionStatusQueryHandler(
	ITillerApiClient tillerApiClient,
	ITillerSelectionService selectionService,
	ITillerSyncService syncService) : Mediator.IRequestHandler<GetTillerConnectionStatusQuery, TillerConnectionStatus>
{
	public async ValueTask<TillerConnectionStatus> Handle(
		GetTillerConnectionStatusQuery request,
		CancellationToken cancellationToken)
	{
		if (!tillerApiClient.IsConfigured)
		{
			return new TillerConnectionStatus(false, false, null, null);
		}

		bool isConnected;
		try
		{
			await tillerApiClient.ListSpreadsheetsAsync(cancellationToken);
			isConnected = true;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			isConnected = false;
		}

		string? selected = await selectionService.GetSelectedSpreadsheetIdAsync(cancellationToken);
		TillerSyncStatus sync = await syncService.GetSyncStatusAsync(cancellationToken);
		return new TillerConnectionStatus(true, isConnected, selected, sync.LastSuccessfulSyncUtc);
	}
}

public record GetTillerSpreadsheetsQuery : IQuery<IReadOnlyList<TillerSpreadsheet>>;

public class GetTillerSpreadsheetsQueryHandler(
	ITillerApiClient tillerApiClient) : Mediator.IRequestHandler<GetTillerSpreadsheetsQuery, IReadOnlyList<TillerSpreadsheet>>
{
	public async ValueTask<IReadOnlyList<TillerSpreadsheet>> Handle(
		GetTillerSpreadsheetsQuery request,
		CancellationToken cancellationToken)
	{
		return await tillerApiClient.ListSpreadsheetsAsync(cancellationToken);
	}
}

public record GetSelectedTillerSpreadsheetQuery : IQuery<string?>;

public class GetSelectedTillerSpreadsheetQueryHandler(
	ITillerSelectionService selectionService) : Mediator.IRequestHandler<GetSelectedTillerSpreadsheetQuery, string?>
{
	public async ValueTask<string?> Handle(
		GetSelectedTillerSpreadsheetQuery request,
		CancellationToken cancellationToken)
	{
		return await selectionService.GetSelectedSpreadsheetIdAsync(cancellationToken);
	}
}

public record GetTillerAccountsQuery : IQuery<IReadOnlyList<TillerAccount>>;

public class GetTillerAccountsQueryHandler(
	ITillerApiClient tillerApiClient) : Mediator.IRequestHandler<GetTillerAccountsQuery, IReadOnlyList<TillerAccount>>
{
	public async ValueTask<IReadOnlyList<TillerAccount>> Handle(
		GetTillerAccountsQuery request,
		CancellationToken cancellationToken)
	{
		return await tillerApiClient.ListAccountsAsync(cancellationToken);
	}
}

public record GetTillerCategoriesQuery : IQuery<IReadOnlyList<TillerCategory>>;

public class GetTillerCategoriesQueryHandler(
	ITillerApiClient tillerApiClient,
	ITillerSelectionService selectionService) : Mediator.IRequestHandler<GetTillerCategoriesQuery, IReadOnlyList<TillerCategory>>
{
	public async ValueTask<IReadOnlyList<TillerCategory>> Handle(
		GetTillerCategoriesQuery request,
		CancellationToken cancellationToken)
	{
		string? spreadsheetId = await selectionService.GetSelectedSpreadsheetIdAsync(cancellationToken);
		if (string.IsNullOrWhiteSpace(spreadsheetId))
		{
			return [];
		}

		IReadOnlyList<TillerCategory> categories = await tillerApiClient.ListCategoriesAsync(spreadsheetId, cancellationToken);

		// Hidden categories never classify bank rows; drop them like the
		// YNAB surface does so the settings UI only offers usable values.
		return categories.Where(c => !c.HideFromReports).ToList();
	}
}
