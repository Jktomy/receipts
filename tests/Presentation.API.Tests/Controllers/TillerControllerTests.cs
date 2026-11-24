using API.Controllers;
using API.Http;
using API.Tiller;
using Application.Commands.Tiller;
using Application.Interfaces.Services;
using Application.Models.Tiller;
using Application.Queries.Core.Tiller;
using FluentAssertions;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace Presentation.API.Tests.Controllers;

public class TillerControllerTests
{
	private readonly Mock<IMediator> _mediatorMock = new();
	private readonly TillerMapper _mapper = new();
	private readonly Mock<ILogger<TillerController>> _loggerMock = ControllerTestHelpers.GetLoggerMock<TillerController>();
	private readonly TillerController _controller;

	public TillerControllerTests()
	{
		_controller = new TillerController(_mediatorMock.Object, _mapper, _loggerMock.Object)
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext(),
			},
		};
	}

	[Fact]
	public async Task GetTransactions_RejectsNegativeOffsetWithoutSend()
	{
		// Act
		var result = await _controller.GetTransactions(offset: -1, cancellationToken: CancellationToken.None);

		// Assert
		result.Result.Should().BeOfType<BadRequestProblem>();
		_mediatorMock.Verify(m => m.Send(It.IsAny<GetTillerTransactionsQuery>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task GetTransactions_RejectsUnknownSortWithoutSend()
	{
		// Act
		var result = await _controller.GetTransactions(sortBy: "bogus", cancellationToken: CancellationToken.None);

		// Assert
		result.Result.Should().BeOfType<BadRequestProblem>();
		_mediatorMock.Verify(m => m.Send(It.IsAny<GetTillerTransactionsQuery>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task GetTransactions_ReturnsMappedPage()
	{
		// Arrange
		_mediatorMock.Setup(m => m.Send(It.IsAny<GetTillerTransactionsQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TillerTransactionListResult(
				[new TillerStoredTransaction(
					Guid.NewGuid(), "t-1", new DateOnly(2026, 11, 1), "WHOLEFDS", -9.99m,
					"Groceries", "Chase Checking", "Chase", "posted", null, 1, "auto", Guid.NewGuid())],
				1));

		// Act
		var result = await _controller.GetTransactions();

		// Assert
		Ok<TillerTransactionListDto> ok = result.Result.Should().BeOfType<Ok<TillerTransactionListDto>>().Subject;
		ok.Value!.Total.Should().Be(1);
		ok.Value.Rows.Should().ContainSingle().Which.LinkKind.Should().Be("auto");
	}

	[Fact]
	public async Task Sync_RejectsUnknownModeWithoutSend()
	{
		// Act
		var result = await _controller.Sync(new SyncTillerRequest("sometimes"), CancellationToken.None);

		// Assert
		result.Result.Should().BeOfType<BadRequestProblem>();
		_mediatorMock.Verify(m => m.Send(It.IsAny<SyncTillerCommand>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task Sync_ReturnsMappedCounts()
	{
		// Arrange
		_mediatorMock.Setup(m => m.Send(It.IsAny<SyncTillerCommand>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TillerSyncResult(5, 1, 0, 2, 0, 3));

		// Act
		var result = await _controller.Sync(new SyncTillerRequest("full"), CancellationToken.None);

		// Assert
		Ok<TillerSyncResultDto> ok = result.Result.Should().BeOfType<Ok<TillerSyncResultDto>>().Subject;
		ok.Value!.Inserted.Should().Be(5);
		ok.Value.Pages.Should().Be(3);
	}

	[Fact]
	public async Task GetSuggestion_MapsDecisionToLowercase()
	{
		// Arrange
		Guid receiptId = Guid.NewGuid();
		_mediatorMock.Setup(m => m.Send(It.IsAny<GetTillerMatchSuggestionQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TillerMatchSuggestion(TillerMatchDecision.Linked, receiptId, "exact", []));

		// Act
		var result = await _controller.GetSuggestion("t-1", CancellationToken.None);

		// Assert
		Ok<TillerMatchSuggestionDto> ok = result.Result.Should().BeOfType<Ok<TillerMatchSuggestionDto>>().Subject;
		ok.Value!.Decision.Should().Be("linked");
		ok.Value.ReceiptId.Should().Be(receiptId);
	}

	[Fact]
	public async Task GetSpreadsheetSettings_ReturnsObjectWithNullWhenUnset()
	{
		// Arrange: wire contract is ALWAYS an object, never a bare string or 404.
		_mediatorMock.Setup(m => m.Send(It.IsAny<GetSelectedTillerSpreadsheetQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((string?)null);

		// Act
		Ok<TillerSelectedSpreadsheetDto> ok = await _controller.GetSpreadsheetSettings(CancellationToken.None);

		// Assert
		ok.Value!.SelectedSpreadsheetId.Should().BeNull();
	}

	[Fact]
	public async Task SelectSpreadsheet_ReturnsObjectEcho()
	{
		// Arrange
		_mediatorMock.Setup(m => m.Send(It.IsAny<SelectTillerSpreadsheetCommand>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Mediator.Unit.Value);

		// Act
		var result = await _controller.SelectSpreadsheet(
			new SelectTillerSpreadsheetRequest("sheet-1"), CancellationToken.None);

		// Assert
		Ok<TillerSelectedSpreadsheetDto> ok = result.Result.Should().BeOfType<Ok<TillerSelectedSpreadsheetDto>>().Subject;
		ok.Value!.SelectedSpreadsheetId.Should().Be("sheet-1");
	}

	[Fact]
	public async Task GetSuggestion_UnknownTransaction_ReturnsNotFound()
	{
		// Arrange
		_mediatorMock.Setup(m => m.Send(It.IsAny<GetTillerMatchSuggestionQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TillerMatchSuggestion(TillerMatchDecision.Unknown, null, null, []));

		// Act
		var result = await _controller.GetSuggestion("nope", CancellationToken.None);

		// Assert
		result.Result.Should().BeOfType<NotFound>();
	}

	[Fact]
	public async Task ConfirmLink_UnverifiedReceipt_ReturnsBadRequest()
	{
		// Arrange
		_mediatorMock.Setup(m => m.Send(It.IsAny<ConfirmTillerLinkCommand>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("Manual confirm requires a human-verified receipt."));

		// Act
		var result = await _controller.ConfirmLink("t-1", new ConfirmTillerLinkRequest(Guid.NewGuid(), null), CancellationToken.None);

		// Assert
		result.Result.Should().BeOfType<BadRequestProblem>();
	}

	[Fact]
	public async Task GetPaymentContext_MissingLink_ReturnsNotFound()
	{
		// Arrange
		_mediatorMock.Setup(m => m.Send(It.IsAny<GetTillerPaymentContextQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((TillerPaymentContext?)null);

		// Act
		var result = await _controller.GetPaymentContext(Guid.NewGuid(), CancellationToken.None);

		// Assert
		result.Result.Should().BeOfType<NotFound>();
	}

	[Fact]
	public async Task GetEvents_RejectsUnknownOutcomeWithoutSend()
	{
		// Act
		var result = await _controller.GetEvents(outcome: "maybe", cancellationToken: CancellationToken.None);

		// Assert
		result.Result.Should().BeOfType<BadRequestProblem>();
		_mediatorMock.Verify(m => m.Send(It.IsAny<GetTillerSyncEventsQuery>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task GetAnalytics_ReturnsMappedBuckets()
	{
		// Arrange
		_mediatorMock.Setup(m => m.Send(It.IsAny<GetTillerAnalyticsQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TillerAnalyticsResult([], [], [], [], [], 0, -44.49m, 4.49m, "Captured and reviewed receipts only."));

		// Act
		var result = await _controller.GetAnalytics(CancellationToken.None);

		// Assert
		Ok<TillerAnalyticsDto> ok = result.Should().BeOfType<Ok<TillerAnalyticsDto>>().Subject;
		ok.Value!.BankSpendingTotal.Should().Be(-44.49m);
		ok.Value.Alerts.Should().BeEmpty();
	}
}
