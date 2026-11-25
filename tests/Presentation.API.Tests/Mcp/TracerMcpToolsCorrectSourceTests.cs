using API.Mapping.Core;
using API.Mcp;
using API.Tiller;
using Application.Commands.Receipt.Update;
using Application.Queries.Core.Receipt;
using Domain;
using Domain.Core;
using FluentAssertions;
using Mediator;
using Moq;
using Xunit;

namespace Presentation.API.Tests.Mcp;

// TDD slice: receipts_correct forwards optional provenance fields with the
// same validation as the HTTP update contract. Bind-once/no-rebind and the
// paperless-verified guard live in ReceiptRepository.UpdateAsync.
public class TracerMcpToolsCorrectSourceTests
{
	private readonly Mock<IMediator> _mediatorMock = new(MockBehavior.Strict);
	private readonly TracerMcpTools _tools;

	public TracerMcpToolsCorrectSourceTests()
	{
		_tools = new TracerMcpTools(_mediatorMock.Object, new ReceiptMapper(), new TillerMapper());
	}

	private static Receipt CurrentReceipt() => new(
		Guid.Parse("1d5c75c8-e419-4836-b114-1b79d6eefd9c"),
		"Le'DRIP Coffee and Tea",
		new DateOnly(2026, 11, 22),
		new Money(1.8m));

	private void StubCurrent(Receipt current)
	{
		_mediatorMock.Setup(m => m.Send(It.IsAny<GetReceiptByIdQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(current);
	}

	[Fact]
	public async Task Correct_ForwardsSourceKindAndDocumentId()
	{
		StubCurrent(CurrentReceipt());
		UpdateReceiptCommand? sent = null;
		_mediatorMock.Setup(m => m.Send(It.IsAny<UpdateReceiptCommand>(), It.IsAny<CancellationToken>()))
			.Callback<IRequest<bool>, CancellationToken>((request, _) => sent = (UpdateReceiptCommand)request)
			.ReturnsAsync(true);

		bool ok = await _tools.ReceiptsCorrectAsync(
			CurrentReceipt().Id,
			location: null,
			taxAmount: null,
			taxUnknown: null,
			sourceKind: "user-reported",
			sourcePaperlessDocumentId: 336);

		ok.Should().BeTrue();
		sent.Should().NotBeNull();
		sent!.Receipts.Should().ContainSingle();
		sent.Receipts[0].SourceKind.Should().Be("user-reported");
		sent.Receipts[0].SourcePaperlessDocumentId.Should().Be(336);
		sent.Receipts[0].Location.Should().Be("Le'DRIP Coffee and Tea");
	}

	[Fact]
	public async Task Correct_NullSourceFields_PreservesCurrent()
	{
		StubCurrent(CurrentReceipt());
		UpdateReceiptCommand? sent = null;
		_mediatorMock.Setup(m => m.Send(It.IsAny<UpdateReceiptCommand>(), It.IsAny<CancellationToken>()))
			.Callback<IRequest<bool>, CancellationToken>((request, _) => sent = (UpdateReceiptCommand)request)
			.ReturnsAsync(true);

		await _tools.ReceiptsCorrectAsync(CurrentReceipt().Id);

		sent!.Receipts[0].SourceKind.Should().BeNull();
		sent.Receipts[0].SourcePaperlessDocumentId.Should().BeNull();
	}

	[Fact]
	public async Task Correct_InvalidSourceKind_ThrowsWithAdmittedMessage()
	{
		StubCurrent(CurrentReceipt());

		Func<Task> call = () => _tools.ReceiptsCorrectAsync(CurrentReceipt().Id, sourceKind: "bogus-kind");

		await call.Should().ThrowAsync<ArgumentException>()
			.WithMessage($"*{Receipt.SourceKindNotAdmitted}*");
		_mediatorMock.Verify(
			m => m.Send(It.IsAny<UpdateReceiptCommand>(), It.IsAny<CancellationToken>()),
			Times.Never);
	}

	[Fact]
	public async Task Correct_OutOfRangeDocumentId_ThrowsArgumentException()
	{
		StubCurrent(CurrentReceipt());

		Func<Task> call = () => _tools.ReceiptsCorrectAsync(CurrentReceipt().Id, sourcePaperlessDocumentId: 0);

		await call.Should().ThrowAsync<ArgumentException>();
		_mediatorMock.Verify(
			m => m.Send(It.IsAny<UpdateReceiptCommand>(), It.IsAny<CancellationToken>()),
			Times.Never);
	}
}
