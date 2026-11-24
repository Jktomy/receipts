using Application.Commands.Tiller;
using Application.Interfaces.Services;
using Application.Models.Tiller;
using FluentAssertions;
using Moq;

namespace Application.Tests.Commands.Tiller;

public class TillerCommandHandlerTests
{
	[Fact]
	public async Task SelectSpreadsheet_DelegatesToSelectionService()
	{
		// Arrange
		Mock<ITillerSelectionService> selection = new();
		SelectTillerSpreadsheetCommandHandler handler = new(selection.Object);

		// Act
		await handler.Handle(new SelectTillerSpreadsheetCommand("sheet-1"), CancellationToken.None);

		// Assert
		selection.Verify(s => s.SelectSpreadsheetAsync("sheet-1", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Theory]
	[InlineData("incremental", true)]
	[InlineData("full", false)]
	[InlineData("FULL", false)]
	public async Task SyncTiller_RoutesModeToSyncService(string mode, bool expectIncremental)
	{
		// Arrange
		Mock<ITillerSyncService> sync = new();
		sync.Setup(s => s.SyncFullAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TillerSyncResult(1, 0, 0, 0, 0, 1));
		sync.Setup(s => s.SyncIncrementalAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TillerSyncResult(2, 0, 0, 0, 0, 1));
		SyncTillerCommandHandler handler = new(sync.Object);

		// Act
		TillerSyncResult result = await handler.Handle(new SyncTillerCommand(mode), CancellationToken.None);

		// Assert
		result.Inserted.Should().Be(expectIncremental ? 2 : 1);
		sync.Verify(
			s => s.SyncIncrementalAsync(It.IsAny<CancellationToken>()),
			expectIncremental ? Times.Once() : Times.Never());
		sync.Verify(
			s => s.SyncFullAsync(It.IsAny<CancellationToken>()),
			expectIncremental ? Times.Never() : Times.Once());
	}

	[Fact]
	public async Task CreateMapping_DelegatesToMappingService()
	{
		// Arrange
		Guid accountId = Guid.NewGuid();
		TillerAccountMappingDto dto = new(Guid.NewGuid(), accountId, "acc-x1", "Chase", "sheet-1");
		Mock<ITillerAccountMappingService> mappings = new();
		mappings.Setup(m => m.CreateAsync(accountId, "acc-x1", "Chase", "sheet-1", It.IsAny<CancellationToken>()))
			.ReturnsAsync(dto);
		CreateTillerAccountMappingCommandHandler handler = new(mappings.Object);

		// Act
		TillerAccountMappingDto result = await handler.Handle(
			new CreateTillerAccountMappingCommand(accountId, "acc-x1", "Chase", "sheet-1"),
			CancellationToken.None);

		// Assert
		result.Should().Be(dto);
	}

	[Fact]
	public async Task UpdateMapping_DelegatesToMappingService()
	{
		// Arrange
		Mock<ITillerAccountMappingService> mappings = new();
		UpdateTillerAccountMappingCommandHandler handler = new(mappings.Object);
		Guid id = Guid.NewGuid();

		// Act
		await handler.Handle(new UpdateTillerAccountMappingCommand(id, "acc-x2", "Savings", "sheet-1"), CancellationToken.None);

		// Assert
		mappings.Verify(m => m.UpdateAsync(id, "acc-x2", "Savings", "sheet-1", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task DeleteMapping_DelegatesToMappingService()
	{
		// Arrange
		Mock<ITillerAccountMappingService> mappings = new();
		DeleteTillerAccountMappingCommandHandler handler = new(mappings.Object);
		Guid id = Guid.NewGuid();

		// Act
		await handler.Handle(new DeleteTillerAccountMappingCommand(id), CancellationToken.None);

		// Assert
		mappings.Verify(m => m.DeleteAsync(id, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task ConfirmLink_DelegatesToMatchingService()
	{
		// Arrange
		Guid receiptId = Guid.NewGuid();
		TillerMatchSuggestion suggestion = new(TillerMatchDecision.Manual, receiptId, null, []);
		Mock<ITillerMatchingService> matching = new();
		matching.Setup(m => m.ConfirmAsync("t-1", receiptId, "anna", It.IsAny<CancellationToken>()))
			.ReturnsAsync(suggestion);
		ConfirmTillerLinkCommandHandler handler = new(matching.Object);

		// Act
		TillerMatchSuggestion result = await handler.Handle(
			new ConfirmTillerLinkCommand("t-1", receiptId, "anna"), CancellationToken.None);

		// Assert
		result.Should().Be(suggestion);
	}

	[Fact]
	public async Task Unlink_DelegatesToMatchingService()
	{
		// Arrange
		TillerMatchSuggestion suggestion = new(TillerMatchDecision.Ambiguous, null, null, []);
		Mock<ITillerMatchingService> matching = new();
		matching.Setup(m => m.UnlinkAsync("t-1", "anna", It.IsAny<CancellationToken>()))
			.ReturnsAsync(suggestion);
		UnlinkTillerLinkCommandHandler handler = new(matching.Object);

		// Act
		TillerMatchSuggestion result = await handler.Handle(
			new UnlinkTillerLinkCommand("t-1", "anna"), CancellationToken.None);

		// Assert
		result.Should().Be(suggestion);
	}
}
