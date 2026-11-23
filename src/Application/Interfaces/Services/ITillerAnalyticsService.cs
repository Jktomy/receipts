using Application.Models.Tiller;

namespace Application.Interfaces.Services;

public interface ITillerAnalyticsService
{
	Task<TillerAnalyticsResult> GetAnalyticsAsync(CancellationToken cancellationToken);
}
