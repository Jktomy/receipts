using System.Security.Claims;
using System.Text.Json;
using API.Authentication;
using API.Generated.Dtos;
using API.Http;
using Application.Interfaces.Services;
using Asp.Versioning;
using Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

[ApiVersion("1.0")]
[ApiController]
[Route("api/apikeys")]
[Authorize]
[EnableRateLimiting("api-key")]
public class ApiKeyController(
	IApiKeyService apiKeyService,
	IAuthAuditService authAuditService,
	ILogger<ApiKeyController> logger) : ControllerBase
{
	[HttpGet]
	[EndpointSummary("Get all API keys for the authenticated user")]
	public async Task<Results<Ok<List<ApiKeyResponse>>, UnauthorizedHttpResult>> GetApiKeys()
	{
		string? userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (userId is null)
		{
			return TypedResults.Unauthorized();
		}

		IReadOnlyList<ApiKeyInfo> keys = await apiKeyService.GetApiKeysForUserAsync(userId);
		List<ApiKeyResponse> response = [.. keys.Select(k => new ApiKeyResponse
		{
			Id = k.Id,
			Name = k.Name,
			CreatedAt = k.CreatedAt,
			LastUsedAt = k.LastUsedAt,
			ExpiresAt = k.ExpiresAt,
			IsRevoked = k.IsRevoked,
			BypassRateLimit = k.BypassRateLimit,
			Scopes = [.. k.Scopes],
		})];

		return TypedResults.Ok(response);
	}

	[HttpPost]
	[EndpointSummary("Create a new API key")]
	public async Task<Results<Ok<CreateApiKeyResponse>, UnauthorizedHttpResult, ForbidHttpResult, BadRequestProblem>> CreateApiKey([FromBody] CreateApiKeyRequest request)
	{
		string? userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (userId is null)
		{
			return TypedResults.Unauthorized();
		}

		if (AuthenticatedByApiKey(User))
		{
			return TypedResults.Forbid();
		}

		bool bypassRateLimit = request.BypassRateLimit;
		if (bypassRateLimit && !User.IsInRole(AppRoles.Admin))
		{
			return TypedResults.Forbid();
		}

		// Scopes are OPTIONAL, not a release requirement: an absent or empty list
		// issues one ordinary full key (NULL stored = owner's full account rights,
		// the pre-scope behavior). A non-empty list only narrows the key and every
		// entry must be admitted; the service canonicalizes before persisting.
		IReadOnlyList<string>? scopes = (request.Scopes is null || request.Scopes.Count == 0)
			? null
			: [.. request.Scopes];
		if (scopes is not null && scopes.Any(s => !ApiKeyScopes.IsAdmitted(s)))
		{
			return ApiProblem.BadRequest(
				"Unknown scope. Admitted scopes: " + string.Join(", ", ApiKeyScopes.All));
		}
		string? canonicalScopes = scopes is null ? null : ApiKeyScopes.Canonicalize(scopes);

		CreateApiKeyResult created;
		try
		{
			created = await apiKeyService.CreateApiKeyAsync(userId, request.Name, request.ExpiresAt, bypassRateLimit, scopes);
		}
		catch (ArgumentException ex)
		{
			return ApiProblem.BadRequest(ex.Message);
		}

		await LogAuthEventAsync(nameof(AuthEventType.ApiKeyCreated), userId, null, true, null, created.Id,
			JsonSerializer.Serialize(new { bypassRateLimit, scopes = canonicalScopes }));

		return TypedResults.Ok(new CreateApiKeyResponse
		{
			Id = created.Id,
			Name = request.Name,
			RawKey = created.RawKey,
			CreatedAt = created.CreatedAt,
			ExpiresAt = request.ExpiresAt,
			BypassRateLimit = bypassRateLimit,
			Scopes = [.. ApiKeyScopes.Parse(canonicalScopes)],
		});
	}

	[HttpDelete("{id}")]
	[EndpointSummary("Revoke an API key")]
	public async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult>> RevokeApiKey([FromRoute] Guid id)
	{
		string? userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (userId is null)
		{
			return TypedResults.Unauthorized();
		}

		if (AuthenticatedByApiKey(User))
		{
			return TypedResults.Forbid();
		}

		await apiKeyService.RevokeApiKeyAsync(id, userId);
		await LogAuthEventAsync(nameof(AuthEventType.ApiKeyRevoked), userId, null, true, null, id);
		return TypedResults.NoContent();
	}

	// Key-authenticated callers — any API key, scoped or full — can never manage
	// keys. Key management stays an owner JWT/session operation. The scheme is set
	// by the auth middleware and cannot be asserted by the caller; admin-role gating
	// is NOT sufficient because key principals inherit their owner's roles.
	private static bool AuthenticatedByApiKey(ClaimsPrincipal user) =>
		string.Equals(user.Identity?.AuthenticationType,
			ApiKeyAuthenticationDefaults.AuthenticationScheme, StringComparison.Ordinal);

	private async Task LogAuthEventAsync(string eventType, string? userId, string? username, bool success, string? failureReason = null, Guid? apiKeyId = null, string? metadataJson = null)
	{
		try
		{
			await authAuditService.LogAsync(new Application.Interfaces.Services.AuthAuditEntryDto(
				Guid.NewGuid(),
				eventType,
				userId,
				apiKeyId,
				username,
				success,
				failureReason,
				HttpContext.Connection.RemoteIpAddress?.ToString(),
				Request.Headers.UserAgent.ToString(),
				DateTimeOffset.UtcNow,
				metadataJson));
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to log auth audit event {EventType}", eventType);
		}
	}
}
