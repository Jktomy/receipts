using API.Mapping.Core;
using API.Mcp;
using API.Tiller;
using Application.Commands.Receipt.CreateComplete;
using Application.Commands.Receipt.Update;
using Application.Commands.Tiller;
using Application.Interfaces.Services;
using Application.Models;
using Application.Models.Tiller;
using Application.Queries.Core.Receipt;
using Application.Queries.Core.Tiller;
using Domain.Core;
using Domain;
using FluentAssertions;
using Mediator;
using Moq;
using Xunit;

namespace Presentation.API.Tests.Mcp;

// TDD slice: fixed typed MCP face over the existing CQRS contract.
// No keymgmt/admin/delete surface; guards mirror the HTTP contract.
public class TracerMcpToolsTests
{
	private readonly Mock<IMediator> _mediatorMock = new(MockBehavior.Strict);
	private readonly TracerMcpTools _tools;

	public TracerMcpToolsTests()
	{
		_tools = new TracerMcpTools(_mediatorMock.Object, new ReceiptMapper(), new TillerMapper());
	}

	[Fact]
	public async Task ReceiptsList_DelegatesWithDefaults()
	{
		_mediatorMock.Setup(m => m.Send(It.IsAny<GetAllReceiptsQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new Application.Models.PagedResult<Application.Models.ReceiptListItem>([], 0, 0, 25));

		var result = await _tools.ReceiptsListAsync(25, 0);

		result.Total.Should().Be(0);
		_mediatorMock.Verify(m => m.Send(
			It.Is<GetAllReceiptsQuery>(q => q.Limit == 25 && q.Offset == 0),
			It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task ReceiptsGet_EmptyId_Throws()
	{
		await Assert.ThrowsAsync<ArgumentException>(() => _tools.ReceiptsGetAsync(Guid.Empty));
		_mediatorMock.VerifyNoOtherCalls();
	}

	[Fact]
	public async Task ReceiptsCreate_EmptyLocation_ThrowsWithoutMediatorCall()
	{
		await Assert.ThrowsAsync<ArgumentException>(() =>
			_tools.ReceiptsCreateAsync("", "2026-11-22", [], 0m, false));
		_mediatorMock.VerifyNoOtherCalls();
	}

	[Fact]
	public async Task ReceiptsCreate_Valid_SendsCompleteCommandWithItems()
	{
		var created = new Receipt(Guid.NewGuid(), "MCP Store", new DateOnly(2026, 11, 22), new Money(1.80m));
		_mediatorMock.Setup(m => m.Send(It.IsAny<CreateCompleteReceiptCommand>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new CreateCompleteReceiptResult(created, [], [], []));

		var result = await _tools.ReceiptsCreateAsync(
			"MCP Store", "2026-11-22",
			[new McpReceiptItemInput("Milk", 2m, 3.50m)],
			1.80m, false);

		result.Location.Should().Be("MCP Store");
		_mediatorMock.Verify(m => m.Send(
			It.Is<CreateCompleteReceiptCommand>(c =>
				c.Receipt.Location == "MCP Store" &&
				c.Items.Count == 1 &&
				c.Items[0].Description == "Milk" &&
				c.Items[0].Quantity == 2m),
			It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task TillerTransactions_DelegatesQuery()
	{
		_mediatorMock.Setup(m => m.Send(It.IsAny<GetTillerTransactionsQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TillerTransactionListResult([], 0));

		var result = await _tools.TillerTransactionsAsync(10, 0, true, "Midas");

		result.Total.Should().Be(0);
		_mediatorMock.Verify(m => m.Send(
			It.Is<GetTillerTransactionsQuery>(q => q.Limit == 10 && q.UnlinkedOnly && q.Account == "Midas"),
			It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task TillerSuggest_DelegatesWithProviderId()
	{
		_mediatorMock.Setup(m => m.Send(It.IsAny<GetTillerMatchSuggestionQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TillerMatchSuggestion(TillerMatchDecision.None, null, null, []));

		await _tools.TillerSuggestAsync("txn-123");

		_mediatorMock.Verify(m => m.Send(
			It.Is<GetTillerMatchSuggestionQuery>(q => q.ProviderTransactionId == "txn-123"),
			It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task TillerConfirmAsync_PassesReviewerAndIds()
	{
		var receiptId = Guid.NewGuid();
		_mediatorMock.Setup(m => m.Send(It.IsAny<ConfirmTillerLinkCommand>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TillerMatchSuggestion(TillerMatchDecision.Linked, null, null, []));

		await _tools.TillerConfirmAsync("txn-123", receiptId);

		_mediatorMock.Verify(m => m.Send(
			It.Is<ConfirmTillerLinkCommand>(c =>
				c.ProviderTransactionId == "txn-123" &&
				c.ReceiptId == receiptId &&
				!string.IsNullOrWhiteSpace(c.Reviewer)),
			It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task ReceiptsCreateAsync_ForwardsClientRequestId()
	{
		var created = new Receipt(Guid.NewGuid(), "MCP Store", new DateOnly(2026, 11, 22), new Money(1.80m));
		var key = Guid.NewGuid();
		_mediatorMock.Setup(m => m.Send(It.IsAny<CreateCompleteReceiptCommand>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new CreateCompleteReceiptResult(created, [], [], []));

		await _tools.ReceiptsCreateAsync("MCP Store", "2026-11-22", [], 1.80m, false, key.ToString());

		_mediatorMock.Verify(m => m.Send(
			It.Is<CreateCompleteReceiptCommand>(c => c.ClientRequestId == key),
			It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task ReceiptsCreateAsync_SameKeyTwice_SendsSameIdBothTimes()
	{
		var created = new Receipt(Guid.NewGuid(), "MCP Store", new DateOnly(2026, 11, 22), new Money(1.80m));
		var key = Guid.NewGuid();
		_mediatorMock.Setup(m => m.Send(It.IsAny<CreateCompleteReceiptCommand>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new CreateCompleteReceiptResult(created, [], [], []));
		var ids = new List<Guid?>();
		_mediatorMock.Setup(m => m.Send(It.IsAny<CreateCompleteReceiptCommand>(), It.IsAny<CancellationToken>()))
			.Callback<Mediator.IRequest<CreateCompleteReceiptResult>, CancellationToken>((c, _) => ids.Add(((CreateCompleteReceiptCommand)c).ClientRequestId))
			.ReturnsAsync(new CreateCompleteReceiptResult(created, [], [], []));

		await _tools.ReceiptsCreateAsync("MCP Store", "2026-11-22", [], 0m, true, key.ToString());
		await _tools.ReceiptsCreateAsync("MCP Store", "2026-11-22", [], 0m, true, key.ToString());

		ids.Should().HaveCount(2);
		ids.Should().OnlyContain(id => id == key);
	}

	[Fact]
	public async Task ReceiptsCreateAsync_MapsItemFieldsToDomain()
	{
		var created = new Receipt(Guid.NewGuid(), "MCP Store", new DateOnly(2026, 11, 22), new Money(1.80m));
		CreateCompleteReceiptCommand? sent = null;
		_mediatorMock.Setup(m => m.Send(It.IsAny<CreateCompleteReceiptCommand>(), It.IsAny<CancellationToken>()))
			.Callback<Mediator.IRequest<CreateCompleteReceiptResult>, CancellationToken>((c, _) => sent = (CreateCompleteReceiptCommand)c)
			.ReturnsAsync(new CreateCompleteReceiptResult(created, [], [], []));

		await _tools.ReceiptsCreateAsync("MCP Store", "2026-11-22",
			[new McpReceiptItemInput("Beans", 2m, 3.50m, "Coffee")], 0m, true);

		sent.Should().NotBeNull();
		sent!.Items.Should().HaveCount(1);
		sent.Items[0].Description.Should().Be("Beans");
		sent.Items[0].Category.Should().Be("Coffee");
		sent.Items[0].TotalAmount.Amount.Should().Be(7.00m);
	}

	[Fact]
	public async Task ReceiptsCreateAsync_BadClientRequestId_ThrowsClean()
	{
		Func<Task> act = () => _tools.ReceiptsCreateAsync("MCP Store", "2026-11-22", [], 0m, true, "not-a-guid");

		(await Xunit.Assert.ThrowsAsync<ArgumentException>(act)).Message.Should().Contain("clientRequestId");
		_mediatorMock.VerifyNoOtherCalls();
	}

	[Fact]
	public async Task ReceiptsCreateAsync_BadDate_ThrowsCleanWithoutWriterCall()
	{
		Func<Task> act = () => _tools.ReceiptsCreateAsync("MCP Store", "not-a-date", [], 0m, true);

		(await Xunit.Assert.ThrowsAsync<ArgumentException>(act)).Message.Should().Contain("date");
		_mediatorMock.VerifyNoOtherCalls();
	}

	[Fact]
	public void ToolSurface_ContainsNoKeyManagement()
	{
		var methodNames = typeof(TracerMcpTools).GetMethods()
			.Select(m => m.Name)
			.ToList();

		methodNames.Should().NotContain(n =>
			n.Contains("ApiKey", StringComparison.OrdinalIgnoreCase) ||
			n.Contains("Revoke", StringComparison.OrdinalIgnoreCase) ||
			n.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
			n.Contains("Purge", StringComparison.OrdinalIgnoreCase));
	}
}
