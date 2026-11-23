using Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace API.Filters;

[AttributeUsage(AttributeTargets.Method)]
public class RequireTillerConfiguredAttribute : TypeFilterAttribute
{
	public RequireTillerConfiguredAttribute() : base(typeof(RequireTillerConfiguredFilter))
	{
	}

	private class RequireTillerConfiguredFilter(
		ITillerApiClient tillerClient,
		ILogger<RequireTillerConfiguredAttribute> logger) : IAsyncActionFilter
	{
		public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			if (!tillerClient.IsConfigured)
			{
				logger.LogWarning("Tiller read interface is not configured (TILLER_BASE_URL / TILLER_ACCOUNT_ID)");
				context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
				return;
			}

			await next();
		}
	}
}
