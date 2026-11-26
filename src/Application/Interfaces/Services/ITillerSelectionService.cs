using Application.Models.Tiller;

namespace Application.Interfaces.Services;

public interface ITillerSelectionService
{
	Task<string?> GetSelectedSpreadsheetIdAsync(CancellationToken cancellationToken);

	Task<TillerExecutorMetadata?> GetExecutorMetadataAsync(CancellationToken cancellationToken);

	Task SelectSpreadsheetAsync(string spreadsheetId, CancellationToken cancellationToken);
}
