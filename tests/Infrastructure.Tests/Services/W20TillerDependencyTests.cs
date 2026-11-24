using Application.Interfaces.Services;
using FluentAssertions;
using Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Tests.Services;

// W20 integration hunk (TDD RED first): the native Tiller read stack must
// resolve from the REAL application DI registration, with the HttpClient
// BaseAddress bound from server-side configuration. No provider calls here.
public class W20TillerDependencyTests
{
	private static IServiceProvider BuildProvider()
	{
		IConfiguration configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["TILLER_BASE_URL"] = "https://executor.test/",
				["TILLER_ACCOUNT_ID"] = "acc-test",
			})
			.Build();
		IServiceCollection services = new ServiceCollection();
		// Mirrors the real host, which always provides IConfiguration.
		services.AddSingleton<IConfiguration>(configuration);
		services.RegisterInfrastructureServices(configuration);
		return services.BuildServiceProvider();
	}

	[Fact]
	public void Tiller_read_stack_resolves_from_real_registration()
	{
		IServiceProvider provider = BuildProvider();
		provider.GetRequiredService<ITillerApiClient>().Should().NotBeNull();
		provider.GetRequiredService<ITillerSelectionService>().Should().NotBeNull();
		provider.GetRequiredService<ITillerAccountMappingService>().Should().NotBeNull();
		provider.GetRequiredService<ITillerSyncService>().Should().NotBeNull();
		provider.GetRequiredService<ITillerMatchingService>().Should().NotBeNull();
		provider.GetRequiredService<ITillerReceiptCatalog>().Should().NotBeNull();
		provider.GetRequiredService<ITillerTransactionReader>().Should().NotBeNull();
		provider.GetRequiredService<ITillerAnalyticsService>().Should().NotBeNull();
	}

	[Fact]
	public void Tiller_client_resolves_unconfigured_without_sending()
	{
		IConfiguration configuration = new ConfigurationBuilder().Build();
		ServiceCollection services = new();
		services.AddSingleton<IConfiguration>(configuration);
		services.RegisterInfrastructureServices(configuration);
		IServiceProvider provider = services.BuildServiceProvider();
		Action resolve = () => provider.GetRequiredService<ITillerApiClient>();
		resolve.Should().NotThrow();
		resolve.Should().NotBeNull();
	}

	[Fact]
	public void Tiller_HttpClient_binds_configured_BaseAddress()
	{
		IServiceProvider provider = BuildProvider();
		IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();
		factory.CreateClient("ITillerApiClient").BaseAddress.Should().Be("https://executor.test/");
	}
}
