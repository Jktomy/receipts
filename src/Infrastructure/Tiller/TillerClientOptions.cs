namespace Infrastructure.Tiller;

// Server-side configuration keys for the native Tiller read interface.
// Values live in host configuration (environment), never in chat or code.
// (Integrator hunk: mirror these into Common.ConfigurationVariables.)

// Kept in this slice so the feature branch touches no shared files.
public static class TillerConfigurationKeys
{
	public const string BaseUrl = "TILLER_BASE_URL";
	public const string AccountId = "TILLER_ACCOUNT_ID";
	public const string TokenFile = "TILLER_TOKEN_FILE";
}

public class TillerClientOptions
{
	public int CacheTtlSeconds { get; set; } = 300;
	public int DefaultPageSize { get; set; } = 100;

	// Provider cap: query_transactions returns at most 500 rows per request.
	public int MaxPageSize { get; set; } = 500;
}
