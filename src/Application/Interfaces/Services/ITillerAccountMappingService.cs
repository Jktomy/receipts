using Application.Models.Tiller;

namespace Application.Interfaces.Services;

public interface ITillerAccountMappingService
{
	Task<List<TillerAccountMappingDto>> GetBySpreadsheetIdAsync(string spreadsheetId, CancellationToken cancellationToken);

	Task<TillerAccountMappingDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

	Task<TillerAccountMappingDto> CreateAsync(
		Guid receiptsAccountId,
		string tillerAccountId,
		string tillerAccountName,
		string spreadsheetId,
		CancellationToken cancellationToken);

	Task UpdateAsync(
		Guid id,
		string tillerAccountId,
		string tillerAccountName,
		string spreadsheetId,
		CancellationToken cancellationToken);

	Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
