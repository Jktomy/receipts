using Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;

namespace API.Filters;

[AttributeUsage(AttributeTargets.Method)]
public class RequireTillerConfiguredAttribute : TypeFilterAttribute
{
	public RequireTillerConfiguredAttribute(bool allowExecutorCache = false) : base(typeof(RequireTillerConfiguredFilter))
	{
		Arguments = [allowExecutorCache];
	}

	private class RequireTillerConfiguredFilter(
		ITillerApiClient tillerClient,
		IConfiguration configuration,
		ILogger<RequireTillerConfiguredAttribute> logger,
		bool allowExecutorCache) : IAsyncActionFilter
	{
		public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			if (!tillerClient.IsConfigured
				&& !(allowExecutorCache && !string.IsNullOrWhiteSpace(configuration["TILLER_EXECUTOR_SPREADSHEET_ID"])))
			{
				logger.LogWarning("Tiller read interface is not configured (TILLER_BASE_URL / TILLER_ACCOUNT_ID)");
				context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
				return;
			}

			await next();
		}
	}
}
