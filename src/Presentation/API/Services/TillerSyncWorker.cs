using Application.Interfaces.Services;

namespace API.Services;

// Opt-in native worker. No new host, credentials, or upstream write path.
// A full scan catches older edits even when the provider has no change cursor.
public sealed class TillerSyncWorker(
	IServiceScopeFactory scopeFactory,
	IConfiguration configuration,
	ILogger<TillerSyncWorker> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (!string.Equals(configuration["TILLER_AUTO_SYNC_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		int minutes = int.TryParse(configuration["TILLER_AUTO_SYNC_INTERVAL_MINUTES"], out int configured)
			? configured
			: 60;
		if (minutes is < 15 or > 1440)
		{
			logger.LogWarning("Tiller auto sync disabled: interval must be between 15 and 1440 minutes.");
			return;
		}

		// Let host startup and database initialization finish first.
		await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
				ITillerApiClient client = scope.ServiceProvider.GetRequiredService<ITillerApiClient>();
				ITillerSelectionService selection = scope.ServiceProvider.GetRequiredService<ITillerSelectionService>();
				if (client.IsConfigured && !string.IsNullOrWhiteSpace(await selection.GetSelectedSpreadsheetIdAsync(stoppingToken)))
				{
					ITillerSyncService sync = scope.ServiceProvider.GetRequiredService<ITillerSyncService>();
					await sync.SyncFullAsync(stoppingToken);
				}
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				// Never emit provider payloads or credential-bearing exception details.
				logger.LogWarning("Tiller automatic sync failed ({FailureType}); existing cache retained.", ex.GetType().Name);
			}

			await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken);
		}
	}
}
