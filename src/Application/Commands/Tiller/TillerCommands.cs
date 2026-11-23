using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Models.Tiller;

namespace Application.Commands.Tiller;

public record SelectTillerSpreadsheetCommand(string SpreadsheetId) : ICommand<Mediator.Unit>;

public class SelectTillerSpreadsheetCommandHandler(
	ITillerSelectionService selectionService) : Mediator.IRequestHandler<SelectTillerSpreadsheetCommand, Mediator.Unit>
{
	public async ValueTask<Mediator.Unit> Handle(
		SelectTillerSpreadsheetCommand request,
		CancellationToken cancellationToken)
	{
		await selectionService.SelectSpreadsheetAsync(request.SpreadsheetId, cancellationToken);
		return Mediator.Unit.Value;
	}
}

public record SyncTillerCommand(string Mode) : ICommand<TillerSyncResult>;

public class SyncTillerCommandHandler(
	ITillerSyncService syncService) : Mediator.IRequestHandler<SyncTillerCommand, TillerSyncResult>
{
	public async ValueTask<TillerSyncResult> Handle(
		SyncTillerCommand request,
		CancellationToken cancellationToken)
	{
		return string.Equals(request.Mode, "incremental", StringComparison.OrdinalIgnoreCase)
			? await syncService.SyncIncrementalAsync(cancellationToken)
			: await syncService.SyncFullAsync(cancellationToken);
	}
}

public record CreateTillerAccountMappingCommand(
	Guid ReceiptsAccountId,
	string TillerAccountId,
	string TillerAccountName,
	string SpreadsheetId) : ICommand<TillerAccountMappingDto>;

public class CreateTillerAccountMappingCommandHandler(
	ITillerAccountMappingService mappingService) : Mediator.IRequestHandler<CreateTillerAccountMappingCommand, TillerAccountMappingDto>
{
	public async ValueTask<TillerAccountMappingDto> Handle(
		CreateTillerAccountMappingCommand request,
		CancellationToken cancellationToken)
	{
		return await mappingService.CreateAsync(
			request.ReceiptsAccountId,
			request.TillerAccountId,
			request.TillerAccountName,
			request.SpreadsheetId,
			cancellationToken);
	}
}

public record UpdateTillerAccountMappingCommand(
	Guid Id,
	string TillerAccountId,
	string TillerAccountName,
	string SpreadsheetId) : ICommand<Mediator.Unit>;

public class UpdateTillerAccountMappingCommandHandler(
	ITillerAccountMappingService mappingService) : Mediator.IRequestHandler<UpdateTillerAccountMappingCommand, Mediator.Unit>
{
	public async ValueTask<Mediator.Unit> Handle(
		UpdateTillerAccountMappingCommand request,
		CancellationToken cancellationToken)
	{
		await mappingService.UpdateAsync(
			request.Id,
			request.TillerAccountId,
			request.TillerAccountName,
			request.SpreadsheetId,
			cancellationToken);
		return Mediator.Unit.Value;
	}
}

public record DeleteTillerAccountMappingCommand(Guid Id) : ICommand<Mediator.Unit>;

public class DeleteTillerAccountMappingCommandHandler(
	ITillerAccountMappingService mappingService) : Mediator.IRequestHandler<DeleteTillerAccountMappingCommand, Mediator.Unit>
{
	public async ValueTask<Mediator.Unit> Handle(
		DeleteTillerAccountMappingCommand request,
		CancellationToken cancellationToken)
	{
		await mappingService.DeleteAsync(request.Id, cancellationToken);
		return Mediator.Unit.Value;
	}
}

public record ConfirmTillerLinkCommand(
	string ProviderTransactionId,
	Guid ReceiptId,
	string? Reviewer) : ICommand<TillerMatchSuggestion>;

public class ConfirmTillerLinkCommandHandler(
	ITillerMatchingService matchingService) : Mediator.IRequestHandler<ConfirmTillerLinkCommand, TillerMatchSuggestion>
{
	public async ValueTask<TillerMatchSuggestion> Handle(
		ConfirmTillerLinkCommand request,
		CancellationToken cancellationToken)
	{
		return await matchingService.ConfirmAsync(
			request.ProviderTransactionId,
			request.ReceiptId,
			request.Reviewer,
			cancellationToken);
	}
}

public record UnlinkTillerLinkCommand(
	string ProviderTransactionId,
	string? Reviewer) : ICommand<TillerMatchSuggestion>;

public class UnlinkTillerLinkCommandHandler(
	ITillerMatchingService matchingService) : Mediator.IRequestHandler<UnlinkTillerLinkCommand, TillerMatchSuggestion>
{
	public async ValueTask<TillerMatchSuggestion> Handle(
		UnlinkTillerLinkCommand request,
		CancellationToken cancellationToken)
	{
		return await matchingService.UnlinkAsync(
			request.ProviderTransactionId,
			request.Reviewer,
			cancellationToken);
	}
}
