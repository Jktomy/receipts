namespace Infrastructure.Entities;

public class ApiKeyEntity
{
	public Guid Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public string KeyHash { get; set; } = string.Empty;
	public string UserId { get; set; } = string.Empty;
	public virtual ApplicationUser User { get; set; } = null!;
	public DateTimeOffset CreatedAt { get; set; }
	public DateTimeOffset? LastUsedAt { get; set; }
	public DateTimeOffset? ExpiresAt { get; set; }
	public bool IsRevoked { get; set; }
	public bool BypassRateLimit { get; set; }
	// Canonical comma-separated closed scope set (Common.ApiKeyScopes) or
	// null for legacy keys issued before scopes. Scopes only narrow.
	public string? Scopes { get; set; }
}
