using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace API.Authentication;

// Additional gate for API-key callers only. JWT callers are evaluated by the
// existing role authorization and pass through untouched. Scopes only narrow:
// a scoped key must carry the required scope; legacy keys issued before
// scopes have no ApiKeyScoped marker and keep their owner-wide rights
// (rotation to scoped keys is the documented path off that legacy).
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireKeyScopeAttribute(string scope) : Attribute, IAuthorizationFilter
{
	public void OnAuthorization(AuthorizationFilterContext context)
	{
		if (context.HttpContext.User.FindFirst("ApiKeyId") is null)
		{
			return;
		}

		if (context.HttpContext.User.FindFirst("ApiKeyScoped")?.Value != "true")
		{
			return;
		}

		if (context.HttpContext.User.FindAll("ApiKeyScope").Any(c => c.Value == scope))
		{
			return;
		}

		context.Result = new ObjectResult(new { error = $"API key scope '{scope}' is required." })
		{
			StatusCode = StatusCodes.Status403Forbidden,
		};
	}
}
