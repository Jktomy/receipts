using System.Security.Claims;
using API.Controllers;
using API.Generated.Dtos;
using API.Http;
using Application.Interfaces.Services;
using Common;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace Presentation.API.Tests.Controllers;

public class ApiKeyControllerTests
{
	private readonly Mock<IApiKeyService> _apiKeyServiceMock;
	private readonly Mock<IAuthAuditService> _authAuditServiceMock;
	private readonly Mock<ILogger<ApiKeyController>> _loggerMock;
	private readonly ApiKeyController _controller;

	public ApiKeyControllerTests()
	{
		_apiKeyServiceMock = new Mock<IApiKeyService>();
		_authAuditServiceMock = new Mock<IAuthAuditService>();
		_loggerMock = ControllerTestHelpers.GetLoggerMock<ApiKeyController>();

		_controller = new ApiKeyController(
			_apiKeyServiceMock.Object,
			_authAuditServiceMock.Object,
			_loggerMock.Object);

		_controller.ControllerContext = new ControllerContext
		{
			HttpContext = new DefaultHttpContext()
		};
	}

	private void SetupUserClaims(string userId, params string[] roles)
	{
		List<Claim> claims = [new Claim(ClaimTypes.NameIdentifier, userId)];
		foreach (string role in roles)
		{
			claims.Add(new Claim(ClaimTypes.Role, role));
		}

		ClaimsIdentity identity = new(claims, "TestAuth");
		ClaimsPrincipal principal = new(identity);
		_controller.ControllerContext.HttpContext.User = principal;
	}

	// ── GetApiKeys ──────────────────────────────────────────

	[Fact]
	public async Task GetApiKeys_ReturnsOk_WhenAuthenticated()
	{
		SetupUserClaims("user-123");
		List<ApiKeyInfo> keys =
		[
			new(Guid.NewGuid(), "key-1", DateTimeOffset.UtcNow, null, null, false, false, ["tiller:read"]),
			new(Guid.NewGuid(), "key-2", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow.AddDays(30), false, true, []),
		];
		_apiKeyServiceMock.Setup(s => s.GetApiKeysForUserAsync("user-123", It.IsAny<CancellationToken>())).ReturnsAsync(keys);

		Results<Ok<List<ApiKeyResponse>>, UnauthorizedHttpResult> result = await _controller.GetApiKeys();

		Ok<List<ApiKeyResponse>> okResult = Assert.IsType<Ok<List<ApiKeyResponse>>>(result.Result);
		okResult.Value.Should().HaveCount(2);
		okResult.Value![1].BypassRateLimit.Should().BeTrue();
	}

	[Fact]
	public async Task GetApiKeys_ReturnsUnauthorized_WhenNoClaims()
	{
		Results<Ok<List<ApiKeyResponse>>, UnauthorizedHttpResult> result = await _controller.GetApiKeys();

		Assert.IsType<UnauthorizedHttpResult>(result.Result);
	}

	// ── CreateApiKey ────────────────────────────────────────

	[Fact]
	public async Task CreateApiKey_ReturnsOk_WhenAuthenticated()
	{
		SetupUserClaims("user-123");
		Guid keyId = Guid.NewGuid();
		DateTimeOffset createdAt = DateTimeOffset.UtcNow;

		_apiKeyServiceMock.Setup(s => s.CreateApiKeyAsync("user-123", "my-key", null, false, It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new CreateApiKeyResult("raw-key-value", keyId, createdAt));

		Results<Ok<CreateApiKeyResponse>, UnauthorizedHttpResult, ForbidHttpResult, BadRequestProblem> result = await _controller.CreateApiKey(
			new CreateApiKeyRequest { Name = "my-key", Scopes = ["tiller:read", "receipts:read"] });

		Ok<CreateApiKeyResponse> okResult = Assert.IsType<Ok<CreateApiKeyResponse>>(result.Result);
		okResult.Value!.Id.Should().Be(keyId);
		okResult.Value!.RawKey.Should().Be("raw-key-value");
		okResult.Value!.Name.Should().Be("my-key");
		okResult.Value!.CreatedAt.Should().Be(createdAt);
		okResult.Value!.BypassRateLimit.Should().BeFalse();
		okResult.Value!.Scopes.Should().BeEquivalentTo("receipts:read", "tiller:read");
	}

	[Fact]
	public async Task CreateApiKey_ReturnsOkFullKey_WhenScopesEmpty()
	{
		SetupUserClaims("user-123");
		Guid keyId = Guid.NewGuid();
		DateTimeOffset createdAt = DateTimeOffset.UtcNow;

		_apiKeyServiceMock.Setup(s => s.CreateApiKeyAsync("user-123", "my-key", null, false, null, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new CreateApiKeyResult("raw-key-value", keyId, createdAt));

		Results<Ok<CreateApiKeyResponse>, UnauthorizedHttpResult, ForbidHttpResult, BadRequestProblem> result = await _controller.CreateApiKey(
			new CreateApiKeyRequest { Name = "my-key", Scopes = [] });

		Ok<CreateApiKeyResponse> okResult = Assert.IsType<Ok<CreateApiKeyResponse>>(result.Result);
		okResult.Value!.RawKey.Should().Be("raw-key-value");
		okResult.Value!.Scopes.Should().BeEmpty();
		_apiKeyServiceMock.Verify(s => s.CreateApiKeyAsync("user-123", "my-key", null, false, null, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task CreateApiKey_ReturnsBadRequest_WhenScopeNotAdmitted()
	{
		SetupUserClaims("user-123");

		Results<Ok<CreateApiKeyResponse>, UnauthorizedHttpResult, ForbidHttpResult, BadRequestProblem> result = await _controller.CreateApiKey(
			new CreateApiKeyRequest { Name = "my-key", Scopes = ["tiller:read", "admin"] });

		Assert.IsType<BadRequestProblem>(result.Result);
	}

	[Fact]
	public async Task CreateApiKey_ReturnsUnauthorized_WhenNoClaims()
	{
		Results<Ok<CreateApiKeyResponse>, UnauthorizedHttpResult, ForbidHttpResult, BadRequestProblem> result = await _controller.CreateApiKey(
			new CreateApiKeyRequest { Name = "my-key", Scopes = ["tiller:read"] });

		Assert.IsType<UnauthorizedHttpResult>(result.Result);
	}

	[Fact]
	public async Task CreateApiKey_WithBypass_ReturnsOk_WhenAdmin()
	{
		SetupUserClaims("admin-123", AppRoles.Admin);
		Guid keyId = Guid.NewGuid();
		DateTimeOffset createdAt = DateTimeOffset.UtcNow;

		_apiKeyServiceMock.Setup(s => s.CreateApiKeyAsync("admin-123", "bypass-key", null, true, It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new CreateApiKeyResult("raw-key-value", keyId, createdAt));

		Results<Ok<CreateApiKeyResponse>, UnauthorizedHttpResult, ForbidHttpResult, BadRequestProblem> result = await _controller.CreateApiKey(
			new CreateApiKeyRequest { Name = "bypass-key", BypassRateLimit = true, Scopes = ["tiller:read"] });

		Ok<CreateApiKeyResponse> okResult = Assert.IsType<Ok<CreateApiKeyResponse>>(result.Result);
		okResult.Value!.BypassRateLimit.Should().BeTrue();
	}

	[Fact]
	public async Task CreateApiKey_WithBypass_ReturnsForbid_WhenNotAdmin()
	{
		SetupUserClaims("user-123");

		Results<Ok<CreateApiKeyResponse>, UnauthorizedHttpResult, ForbidHttpResult, BadRequestProblem> result = await _controller.CreateApiKey(
			new CreateApiKeyRequest { Name = "bypass-key", BypassRateLimit = true, Scopes = ["tiller:read"] });

		Assert.IsType<ForbidHttpResult>(result.Result);
	}

	// ── RevokeApiKey ────────────────────────────────────────

	[Fact]
	public async Task RevokeApiKey_ReturnsNoContent_WhenAuthenticated()
	{
		SetupUserClaims("user-123");
		Guid keyId = Guid.NewGuid();

		Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult> result = await _controller.RevokeApiKey(keyId);

		Assert.IsType<NoContent>(result.Result);
		_apiKeyServiceMock.Verify(s => s.RevokeApiKeyAsync(keyId, "user-123", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task RevokeApiKey_ReturnsUnauthorized_WhenNoClaims()
	{
		Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult> result = await _controller.RevokeApiKey(Guid.NewGuid());

		Assert.IsType<UnauthorizedHttpResult>(result.Result);
	}

	private void SetupApiKeyClaims(string userId, Guid keyId, bool scoped)
	{
		List<Claim> claims =
		[
			new Claim(ClaimTypes.NameIdentifier, userId),
			new Claim("ApiKeyId", keyId.ToString()),
		];
		if (scoped)
		{
			claims.Add(new Claim("ApiKeyScoped", "true"));
			claims.Add(new Claim("ApiKeyScope", "receipts:read"));
		}

		ClaimsIdentity identity = new(claims, "ApiKey");
		_controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(identity);
	}

	[Fact]
	public async Task CreateApiKey_ReturnsForbid_WhenScopedApiKey()
	{
		SetupApiKeyClaims("user-123", Guid.NewGuid(), scoped: true);

		Results<Ok<CreateApiKeyResponse>, UnauthorizedHttpResult, ForbidHttpResult, BadRequestProblem> result = await _controller.CreateApiKey(
			new CreateApiKeyRequest { Name = "self-minted", Scopes = [] });

		Assert.IsType<ForbidHttpResult>(result.Result);
	}

	[Fact]
	public async Task RevokeApiKey_ReturnsForbid_WhenScopedApiKey()
	{
		SetupApiKeyClaims("user-123", Guid.NewGuid(), scoped: true);

		Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult> result = await _controller.RevokeApiKey(Guid.NewGuid());

		Assert.IsType<ForbidHttpResult>(result.Result);
		_apiKeyServiceMock.Verify(s => s.RevokeApiKeyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task RevokeApiKey_ReturnsForbid_WhenFullApiKey()
	{
		SetupApiKeyClaims("user-123", Guid.NewGuid(), scoped: false);

		Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult> result = await _controller.RevokeApiKey(Guid.NewGuid());

		Assert.IsType<ForbidHttpResult>(result.Result);
		_apiKeyServiceMock.Verify(s => s.RevokeApiKeyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task CreateApiKey_ReturnsForbid_WhenFullApiKey()
	{
		SetupApiKeyClaims("user-123", Guid.NewGuid(), scoped: false);

		Results<Ok<CreateApiKeyResponse>, UnauthorizedHttpResult, ForbidHttpResult, BadRequestProblem> result = await _controller.CreateApiKey(
			new CreateApiKeyRequest { Name = "self-minted", Scopes = [] });

		Assert.IsType<ForbidHttpResult>(result.Result);
	}
}
