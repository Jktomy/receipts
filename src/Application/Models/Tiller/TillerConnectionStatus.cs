namespace Application.Models.Tiller;

public sealed record TillerConnectionStatus(
	bool IsConfigured,
	bool IsConnected,
	string? SelectedSpreadsheetId,
	DateTimeOffset? LastSuccessfulSyncUtc);
