using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using API.Configuration;
using API.Controllers;
using API.Services;
using Application.Models.Tiller;
using Application.Queries.Core.Tiller;
using FluentAssertions;
using Infrastructure.Services;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Presentation.API.Tests.Configuration;

// W20 integration hunk (RED first): the composed app serves Tiller metadata
// reads and gates live routes WITHOUT any provider touch. Unconfigured host:
// connection-status 200 with isConfigured=false; spreadsheets 503 via
// RequireTillerConfigured before any mediator/provider call (Strict mock).
public sealed class W20TillerMetadataHttpTests
{
	[Fact]
	public async Task Tiller_connection_status_serves_unconfigured_without_provider_calls()
	{
		var mediator = new Mock<IMediator>(MockBehavior.Strict);
		mediator.Setup(x => x.Send(It.IsAny<GetTillerConnectionStatusQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TillerConnectionStatus(false, false, null, null));
		await using var app = await Start(mediator);
		using var client = app.GetTestClient();
		using HttpResponseMessage response = await client.GetAsync("/api/tiller/connection-status");
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		body.RootElement.GetProperty("isConfigured").GetBoolean().Should().BeFalse();
		mediator.Verify(x => x.Send(It.IsAny<GetTillerConnectionStatusQuery>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task Tiller_live_routes_503_when_unconfigured_before_any_call()
	{
		var mediator = new Mock<IMediator>(MockBehavior.Strict);
		await using var app = await Start(mediator);
		using var client = app.GetTestClient();
		using HttpResponseMessage response = await client.GetAsync("/api/tiller/spreadsheets");
		response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
		mediator.VerifyNoOtherCalls();
	}

	[Fact]
	public async Task Executor_cache_routes_serve_linked_metadata_without_enabling_direct_sync()
	{
		var mediator = new Mock<IMediator>(MockBehavior.Strict);
		mediator.Setup(x => x.Send(It.IsAny<GetTillerAccountsQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<TillerAccount> { new("a-1", "Checking", "Bank", "checking", "asset") });
		mediator.Setup(x => x.Send(It.IsAny<GetTillerSpreadsheetsQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<TillerSpreadsheet> { new("sheet-1", "Treasury", true, true, 1) });
		await using var app = await Start(mediator, true);
		using var client = app.GetTestClient();
		using HttpResponseMessage accounts = await client.GetAsync("/api/tiller/accounts");
		accounts.StatusCode.Should().Be(HttpStatusCode.OK);
		using JsonDocument body = JsonDocument.Parse(await accounts.Content.ReadAsStringAsync());
		body.RootElement.GetArrayLength().Should().Be(1);
		using HttpResponseMessage sheets = await client.GetAsync("/api/tiller/spreadsheets");
		sheets.StatusCode.Should().Be(HttpStatusCode.OK);
		using HttpResponseMessage sync = await client.PostAsync("/api/tiller/sync", new StringContent("{\"mode\":\"full\"}", System.Text.Encoding.UTF8, "application/json"));
		sync.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
		mediator.Verify(x => x.Send(It.IsAny<GetTillerAccountsQuery>(), It.IsAny<CancellationToken>()), Times.Once);
		mediator.Verify(x => x.Send(It.IsAny<GetTillerSpreadsheetsQuery>(), It.IsAny<CancellationToken>()), Times.Once);
		mediator.VerifyNoOtherCalls();
	}

	private static async Task<WebApplication> Start(Mock<IMediator> mediator, bool executorManaged = false)
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Logging.SetMinimumLevel(LogLevel.Warning);
		builder.Configuration.Sources.Clear();
		builder.Configuration.AddInMemoryCollection(executorManaged
			? new Dictionary<string, string?> { ["TILLER_EXECUTOR_SPREADSHEET_ID"] = "sheet-1" }
			: new Dictionary<string, string?>());
		builder.Services.AddApplicationServices(builder.Configuration).RegisterProgramServices();
		builder.Services.RegisterInfrastructureServices(builder.Configuration);
		builder.Services.AddAuthorization();
		builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuth>("Test", _ => { });
		builder.Services.AddProblemDetails();
		builder.Services.AddControllers().AddApplicationPart(typeof(TillerController).Assembly);
		builder.Services.AddSingleton(mediator.Object);
		var app = builder.Build();
		app.UseAuthentication();
		app.UseAuthorization();
		app.MapControllers().AllowAnonymous();
		await app.StartAsync();
		return app;
	}

	private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
		: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
	{
		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			ClaimsIdentity identity = new([new Claim(ClaimTypes.Name, "w20-test")], "Test");
			return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
		}
	}
}
