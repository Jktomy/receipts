using Application.Interfaces.Services;
using Application.Models.Tiller;
using Application.Queries.Core.Tiller;
using FluentAssertions;
using Moq;

namespace Application.Tests.Queries.Core.Tiller;

public class TillerQueryHandlerTests
{
	[Fact]
	public async Task ConnectionStatus_Unconfigured_ShortCircuitsWithoutLiveCalls()
	{
		// Arrange
		Mock<ITillerApiClient> client = new();
		client.SetupGet(c => c.IsConfigured).Returns(false);
		Mock<ITillerSelectionService> selection = new();
		Mock<ITillerSyncService> sync = new();
		GetTillerConnectionStatusQueryHandler handler = new(client.Object, selection.Object, sync.Object);

		// Act
		TillerConnectionStatus result = await handler.Handle(new GetTillerConnectionStatusQuery(), CancellationToken.None);

		// Assert
		result.Should().Be(new TillerConnectionStatus(false, false, null, null));
		client.Verify(c => c.ListSpreadsheetsAsync(It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task ConnectionStatus_Configured_ReportsSelectionAndLastSync()
	{
		// Arrange
		Mock<ITillerApiClient> client = new();
		client.SetupGet(c => c.IsConfigured).Returns(true);
		client.Setup(c => c.ListSpreadsheetsAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<TillerSpreadsheet>());
		Mock<ITillerSelectionService> selection = new();
		selection.Setup(s => s.GetSelectedSpreadsheetIdAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync("sheet-1");
		DateTimeOffset lastSync = DateTimeOffset.UtcNow.AddHours(-1);
		Mock<ITillerSyncService> sync = new();
		sync.Setup(s => s.GetSyncStatusAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TillerSyncStatus("sheet-1", new DateOnly(2026, 11, 20), true, lastSync));
		GetTillerConnectionStatusQueryHandler handler = new(client.Object, selection.Object, sync.Object);

		// Act
		TillerConnectionStatus result = await handler.Handle(new GetTillerConnectionStatusQuery(), CancellationToken.None);

		// Assert
		result.Should().Be(new TillerConnectionStatus(true, true, "sheet-1", lastSync));
	}

	[Fact]
	public async Task ConnectionStatus_LiveFailure_ReportsDisconnected()
	{
		// Arrange
		Mock<ITillerApiClient> client = new();
		client.SetupGet(c => c.IsConfigured).Returns(true);
		client.Setup(c => c.ListSpreadsheetsAsync(It.IsAny<CancellationToken>()))
			.ThrowsAsync(new HttpRequestException("down"));
		Mock<ITillerSelectionService> selection = new();
		selection.Setup(s => s.GetSelectedSpreadsheetIdAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((string?)null);
		Mock<ITillerSyncService> sync = new();
		sync.Setup(s => s.GetSyncStatusAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TillerSyncStatus(null, null, false, null));
		GetTillerConnectionStatusQueryHandler handler = new(client.Object, selection.Object, sync.Object);

		// Act
		TillerConnectionStatus result = await handler.Handle(new GetTillerConnectionStatusQuery(), CancellationToken.None);

		// Assert
		result.IsConfigured.Should().BeTrue();
		result.IsConnected.Should().BeFalse();
	}

	[Fact]
	public async Task Spreadsheets_DelegatesToApiClient()
	{
		// Arrange
		List<TillerSpreadsheet> expected = [new("sheet-1", "Tiller Foundation", true, true, 1)];
		Mock<ITillerApiClient> client = new();
		client.Setup(c => c.ListSpreadsheetsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expected);
		GetTillerSpreadsheetsQueryHandler handler = new(client.Object);

		// Act
		IReadOnlyList<TillerSpreadsheet> result = await handler.Handle(new GetTillerSpreadsheetsQuery(), CancellationToken.None);

		// Assert
		result.Should().BeSameAs(expected);
	}

	[Fact]
	public async Task Categories_WithoutSelection_ReturnsEmptyWithoutLiveCall()
	{
		// Arrange
		Mock<ITillerApiClient> client = new();
		Mock<ITillerSelectionService> selection = new();
		selection.Setup(s => s.GetSelectedSpreadsheetIdAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((string?)null);
		GetTillerCategoriesQueryHandler handler = new(client.Object, selection.Object);

		// Act
		IReadOnlyList<TillerCategory> result = await handler.Handle(new GetTillerCategoriesQuery(), CancellationToken.None);

		// Assert
		result.Should().BeEmpty();
		client.Verify(c => c.ListCategoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task Categories_HidesHiddenCategories()
	{
		// Arrange
		Mock<ITillerApiClient> client = new();
		Mock<ITillerSelectionService> selection = new();
		selection.Setup(s => s.GetSelectedSpreadsheetIdAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync("sheet-1");
		client.Setup(c => c.ListCategoriesAsync("sheet-1", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<TillerCategory>
			{
				new("Groceries", "Living", "Expense", false),
				new("Credit Card Payment", "Transfer", "Transfer", true),
			});
		GetTillerCategoriesQueryHandler handler = new(client.Object, selection.Object);

		// Act
		IReadOnlyList<TillerCategory> result = await handler.Handle(new GetTillerCategoriesQuery(), CancellationToken.None);

		// Assert
		result.Should().ContainSingle().Which.Name.Should().Be("Groceries");
	}

	[Fact]
	public async Task Mappings_WithoutSelection_ReturnsEmpty()
	{
		// Arrange
		Mock<ITillerAccountMappingService> mappings = new();
		Mock<ITillerSelectionService> selection = new();
		selection.Setup(s => s.GetSelectedSpreadsheetIdAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((string?)null);
		GetTillerAccountMappingsQueryHandler handler = new(mappings.Object, selection.Object);

		// Act
		List<TillerAccountMappingDto> result = await handler.Handle(
			new GetTillerAccountMappingsQuery(null), CancellationToken.None);

		// Assert
		result.Should().BeEmpty();
	}

	[Fact]
	public async Task Suggestion_DelegatesToMatchingService()
	{
		// Arrange
		TillerMatchSuggestion expected = new(TillerMatchDecision.None, null, null, []);
		Mock<ITillerMatchingService> matching = new();
		matching.Setup(m => m.SuggestAsync("t-1", It.IsAny<CancellationToken>())).ReturnsAsync(expected);
		GetTillerMatchSuggestionQueryHandler handler = new(matching.Object);

		// Act
		TillerMatchSuggestion result = await handler.Handle(
			new GetTillerMatchSuggestionQuery("t-1"), CancellationToken.None);

		// Assert
		result.Should().Be(expected);
	}

	[Fact]
	public async Task Analytics_DelegatesToAnalyticsService()
	{
		// Arrange
		TillerAnalyticsResult expected = new([], [], [], [], [], 0, 0m, 0m, "note");
		Mock<ITillerAnalyticsService> analytics = new();
		analytics.Setup(a => a.GetAnalyticsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expected);
		GetTillerAnalyticsQueryHandler handler = new(analytics.Object);

		// Act
		TillerAnalyticsResult result = await handler.Handle(new GetTillerAnalyticsQuery(), CancellationToken.None);

		// Assert
		result.Should().Be(expected);
	}

	[Fact]
	public async Task PaymentContext_DelegatesToMatchingService()
	{
		// Arrange
		TillerPaymentContext expected = new("t-1", new DateOnly(2026, 11, 1), "WHOLEFDS", -9.99m, "Chase", "Groceries", "auto");
		Mock<ITillerMatchingService> matching = new();
		Guid receiptId = Guid.NewGuid();
		matching.Setup(m => m.GetPaymentContextAsync(receiptId, It.IsAny<CancellationToken>()))
			.ReturnsAsync(expected);
		GetTillerPaymentContextQueryHandler handler = new(matching.Object);

		// Act
		TillerPaymentContext? result = await handler.Handle(
			new GetTillerPaymentContextQuery(receiptId), CancellationToken.None);

		// Assert
		result.Should().Be(expected);
	}
}
