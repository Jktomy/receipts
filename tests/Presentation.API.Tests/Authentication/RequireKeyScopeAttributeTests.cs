using System.Security.Claims;
using API.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace Presentation.API.Tests.Authentication;

public class RequireKeyScopeAttributeTests
{
	private static AuthorizationFilterContext FilterContext(params Claim[] claims)
	{
		DefaultHttpContext httpContext = new();
		httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
		return new AuthorizationFilterContext(
			new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
			[]);
	}

	[Fact]
	public void JwtCaller_PassesThrough()
	{
		// Arrange: no ApiKeyId claim = JWT caller; existing role auth applies.
		AuthorizationFilterContext context = FilterContext(
			new Claim(ClaimTypes.NameIdentifier, "user-1"));

		// Act
		new RequireKeyScopeAttribute("tiller:read").OnAuthorization(context);

		// Assert
		context.Result.Should().BeNull();
	}

	[Fact]
	public void LegacyUnscopedKey_PassesThrough()
	{
		// Arrange: keys issued before scopes carry no marker and keep owner-wide rights.
		AuthorizationFilterContext context = FilterContext(
			new Claim(ClaimTypes.NameIdentifier, "user-1"),
			new Claim("ApiKeyId", Guid.NewGuid().ToString()));

		// Act
		new RequireKeyScopeAttribute("tiller:read").OnAuthorization(context);

		// Assert
		context.Result.Should().BeNull();
	}

	[Fact]
	public void ScopedKey_WithScope_Passes()
	{
		// Arrange
		AuthorizationFilterContext context = FilterContext(
			new Claim(ClaimTypes.NameIdentifier, "user-1"),
			new Claim("ApiKeyId", Guid.NewGuid().ToString()),
			new Claim("ApiKeyScoped", "true"),
			new Claim("ApiKeyScope", "tiller:read"));

		// Act
		new RequireKeyScopeAttribute("tiller:read").OnAuthorization(context);

		// Assert
		context.Result.Should().BeNull();
	}

	[Fact]
	public void ScopedKey_WithoutScope_Returns403()
	{
		// Arrange: scoped key lacking the required scope is narrowed, not widened.
		AuthorizationFilterContext context = FilterContext(
			new Claim(ClaimTypes.NameIdentifier, "user-1"),
			new Claim("ApiKeyId", Guid.NewGuid().ToString()),
			new Claim("ApiKeyScoped", "true"),
			new Claim("ApiKeyScope", "receipts:read"));

		// Act
		new RequireKeyScopeAttribute("tiller:read").OnAuthorization(context);

		// Assert
		ObjectResult? result = context.Result.Should().BeOfType<ObjectResult>().Subject;
		result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
	}
}
