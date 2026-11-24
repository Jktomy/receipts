namespace Application.Interfaces.Services;

public record ApiKeyInfo(
	Guid Id,
	string Name,
	DateTimeOffset CreatedAt,
	DateTimeOffset? LastUsedAt,
	DateTimeOffset? ExpiresAt,
	bool IsRevoked,
	bool BypassRateLimit,
	IReadOnlyList<string> Scopes);

public record CreateApiKeyResult(string RawKey, Guid Id, DateTimeOffset CreatedAt);

public record ApiKeyValidationResult(string UserId, Guid KeyId, bool BypassRateLimit, IReadOnlyList<string> Scopes);

public interface IApiKeyService
{
	Task<CreateApiKeyResult> CreateApiKeyAsync(string userId, string name, DateTimeOffset? expiresAt, bool bypassRateLimit = false, IReadOnlyList<string>? scopes = null, CancellationToken cancellationToken = default);
	Task<IReadOnlyList<ApiKeyInfo>> GetApiKeysForUserAsync(string userId, CancellationToken cancellationToken = default);
	Task RevokeApiKeyAsync(Guid id, string userId, CancellationToken cancellationToken = default);
	Task<int> RevokeAllForUserAsync(string userId, CancellationToken cancellationToken = default);
	Task<ApiKeyValidationResult?> GetUserIdByApiKeyAsync(string rawKey, CancellationToken cancellationToken = default);
}
