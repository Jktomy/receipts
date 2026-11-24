namespace Application.Models.Tiller;

public sealed record TillerAccountMappingDto(
	Guid Id,
	Guid ReceiptsAccountId,
	string TillerAccountId,
	string TillerAccountName,
	string SpreadsheetId);
