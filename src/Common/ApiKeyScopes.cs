namespace Common;

// Closed scope set for API keys. Scopes only NARROW a key: a key-authenticated
// caller must additionally satisfy the endpoint's scope gate, on top of the
// owner's account rights. No scope grants delete, key management, or admin.
public static class ApiKeyScopes
{
	public const string ReceiptsRead = "receipts:read";
	public const string ReceiptsWrite = "receipts:write";
	public const string TillerRead = "tiller:read";
	public const string TillerConfirm = "tiller:confirm";

	public static readonly IReadOnlyList<string> All = [ReceiptsRead, ReceiptsWrite, TillerRead, TillerConfirm];

	public static bool IsAdmitted(string? scope) => scope is not null && All.Contains(scope);

	// Stored canonically: admitted scopes, deduplicated, sorted.
	public static string Canonicalize(IEnumerable<string> scopes) =>
		string.Join(",", scopes.Where(IsAdmitted).Distinct().OrderBy(s => s, StringComparer.Ordinal));

	public static IReadOnlyList<string> Parse(string? stored) =>
		(stored ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Where(IsAdmitted).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
}
