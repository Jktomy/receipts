using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Models.Tiller;
namespace Application.Queries.Core.Tiller;

public record GetTillerMatchSuggestionQuery(string ProviderTransactionId) : IQuery<TillerMatchSuggestion>;

public class GetTillerMatchSuggestionQueryHandler(
	ITillerMatchingService matchingService) : Mediator.IRequestHandler<GetTillerMatchSuggestionQuery, TillerMatchSuggestion>
{
	public async ValueTask<TillerMatchSuggestion> Handle(
		GetTillerMatchSuggestionQuery request,
		CancellationToken cancellationToken)
	{
		return await matchingService.SuggestAsync(request.ProviderTransactionId, cancellationToken);
	}
}

public record GetTillerLinkHistoryQuery(string ProviderTransactionId) : IQuery<IReadOnlyList<TillerLinkHistoryEntry>>;

public class GetTillerLinkHistoryQueryHandler(
	ITillerMatchingService matchingService) : Mediator.IRequestHandler<GetTillerLinkHistoryQuery, IReadOnlyList<TillerLinkHistoryEntry>>
{
	public async ValueTask<IReadOnlyList<TillerLinkHistoryEntry>> Handle(
		GetTillerLinkHistoryQuery request,
		CancellationToken cancellationToken)
	{
		return await matchingService.GetHistoryAsync(request.ProviderTransactionId, cancellationToken);
	}
}

public record GetTillerPaymentContextQuery(Guid ReceiptId) : IQuery<TillerPaymentContext?>;

public class GetTillerPaymentContextQueryHandler(
	ITillerMatchingService matchingService) : Mediator.IRequestHandler<GetTillerPaymentContextQuery, TillerPaymentContext?>
{
	public async ValueTask<TillerPaymentContext?> Handle(
		GetTillerPaymentContextQuery request,
		CancellationToken cancellationToken)
	{
		return await matchingService.GetPaymentContextAsync(request.ReceiptId, cancellationToken);
	}
}

public record GetTillerAnalyticsQuery : IQuery<TillerAnalyticsResult>;

public class GetTillerAnalyticsQueryHandler(
	ITillerAnalyticsService analyticsService) : Mediator.IRequestHandler<GetTillerAnalyticsQuery, TillerAnalyticsResult>
{
	public async ValueTask<TillerAnalyticsResult> Handle(
		GetTillerAnalyticsQuery request,
		CancellationToken cancellationToken)
	{
		return await analyticsService.GetAnalyticsAsync(cancellationToken);
	}
}
