using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Models.Tiller;
using Microsoft.Extensions.Configuration;
namespace Application.Queries.Core.Tiller;

public record GetTillerConnectionStatusQuery : IQuery<TillerConnectionStatus>;

public class GetTillerConnectionStatusQueryHandler(
	ITillerApiClient tillerApiClient,
	ITillerSelectionService selectionService,
	ITillerSyncService syncService,
	IConfiguration? configuration = null) : Mediator.IRequestHandler<GetTillerConnectionStatusQuery, TillerConnectionStatus>
{
	public async ValueTask<TillerConnectionStatus> Handle(
		GetTillerConnectionStatusQuery request,
		CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(configuration?["TILLER_EXECUTOR_SPREADSHEET_ID"]))
		{
			string? selectedSheet = await selectionService.GetSelectedSpreadsheetIdAsync(cancellationToken);
			TillerSyncStatus status = await syncService.GetSyncStatusAsync(cancellationToken);
			// This is freshness of the last delivered snapshot, not a live OAuth probe.
			bool fresh = status.Completed && status.LastSuccessfulSyncUtc is DateTimeOffset last
				&& last >= DateTimeOffset.UtcNow.AddHours(-2);
			return new TillerConnectionStatus(true, fresh, selectedSheet, status.LastSuccessfulSyncUtc, true);
		}
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
	ITillerApiClient tillerApiClient,
	ITillerSelectionService selectionService,
	IConfiguration? configuration = null) : Mediator.IRequestHandler<GetTillerSpreadsheetsQuery, IReadOnlyList<TillerSpreadsheet>>
{
	public async ValueTask<IReadOnlyList<TillerSpreadsheet>> Handle(
		GetTillerSpreadsheetsQuery request,
		CancellationToken cancellationToken)
	{
		if (tillerApiClient.IsConfigured && string.IsNullOrWhiteSpace(configuration?["TILLER_EXECUTOR_SPREADSHEET_ID"]))
		{
			return await tillerApiClient.ListSpreadsheetsAsync(cancellationToken);
		}
		TillerExecutorMetadata metadata = await selectionService.GetExecutorMetadataAsync(cancellationToken)
			?? throw new InvalidOperationException("Waiting for the Executor account catalog.");
		return [metadata.Spreadsheet];
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
	ITillerApiClient tillerApiClient,
	ITillerSelectionService selectionService,
	IConfiguration? configuration = null) : Mediator.IRequestHandler<GetTillerAccountsQuery, IReadOnlyList<TillerAccount>>
{
	public async ValueTask<IReadOnlyList<TillerAccount>> Handle(
		GetTillerAccountsQuery request,
		CancellationToken cancellationToken)
	{
		if (tillerApiClient.IsConfigured && string.IsNullOrWhiteSpace(configuration?["TILLER_EXECUTOR_SPREADSHEET_ID"]))
		{
			return await tillerApiClient.ListAccountsAsync(cancellationToken);
		}
		TillerExecutorMetadata metadata = await selectionService.GetExecutorMetadataAsync(cancellationToken)
			?? throw new InvalidOperationException("Waiting for the Executor account catalog.");
		return metadata.Accounts;
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
