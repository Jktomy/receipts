using Application.Interfaces.Services;
using Application.Models.Tiller;
using FluentAssertions;
using Infrastructure.Entities.Core;
using Infrastructure.Services;
using Infrastructure.Tests.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Infrastructure.Tests.Services;

// Slice 2 (TDD RED): connection settings + account mapping persistence.
// Spreadsheet selection is validated against the LIVE read list; unknown or
// non-editable spreadsheets are rejected before anything is stored.
public class TillerSelectionServiceTests
{
	private readonly IDbContextFactory<ApplicationDbContext> _factory = DbContextHelpers.CreateInMemoryContextFactory();
	private readonly Mock<ITillerApiClient> _clientMock = new();

	private TillerSelectionService CreateService() => new(_factory, _clientMock.Object);

	private void SetupLiveSheets()
	{
		_clientMock.Setup(c => c.ListSpreadsheetsAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<TillerSpreadsheet>
			{
				new("sheet-1", "Tiller Foundation", true, true, 1),
				new("sheet-2", "Archive", false, false, 1),
			});
	}

	[Fact]
	public async Task GetSelectedSpreadsheetId_ReturnsNullWhenNeverSelected()
	{
		// Act
		string? selected = await CreateService().GetSelectedSpreadsheetIdAsync(CancellationToken.None);

		// Assert
		selected.Should().BeNull();
		_clientMock.Verify(c => c.ListSpreadsheetsAsync(It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task SelectSpreadsheet_PersistsEditableSheetFromLiveList()
	{
		// Arrange
		SetupLiveSheets();
		TillerSelectionService service = CreateService();

		// Act
		await service.SelectSpreadsheetAsync("sheet-1", CancellationToken.None);

		// Assert
		(await service.GetSelectedSpreadsheetIdAsync(CancellationToken.None)).Should().Be("sheet-1");
	}

	[Fact]
	public async Task SelectSpreadsheet_RejectsUnknownIdBeforeStoring()
	{
		// Arrange
		SetupLiveSheets();
		TillerSelectionService service = CreateService();

		// Act
		Func<Task> act = () => service.SelectSpreadsheetAsync("sheet-evil", CancellationToken.None);

		// Assert
		await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not in the readable spreadsheet list*");
		(await service.GetSelectedSpreadsheetIdAsync(CancellationToken.None)).Should().BeNull();
	}

	[Fact]
	public async Task SelectSpreadsheet_RejectsNonEditableSheetBeforeStoring()
	{
		// Arrange
		SetupLiveSheets();
		TillerSelectionService service = CreateService();

		// Act
		Func<Task> act = () => service.SelectSpreadsheetAsync("sheet-2", CancellationToken.None);

		// Assert
		await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not readable*");
		(await service.GetSelectedSpreadsheetIdAsync(CancellationToken.None)).Should().BeNull();
	}

	[Fact]
	public async Task SelectSpreadsheet_ReplacesPreviousSelection()
	{
		// Arrange
		_clientMock.Setup(c => c.ListSpreadsheetsAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<TillerSpreadsheet>
			{
				new("sheet-1", "Tiller Foundation", true, true, 1),
				new("sheet-3", "New Foundation", true, true, 0),
			});
		TillerSelectionService service = CreateService();
		await service.SelectSpreadsheetAsync("sheet-1", CancellationToken.None);

		// Act
		await service.SelectSpreadsheetAsync("sheet-3", CancellationToken.None);

		// Assert
		(await service.GetSelectedSpreadsheetIdAsync(CancellationToken.None)).Should().Be("sheet-3");
		await using ApplicationDbContext context = _factory.CreateDbContext();
		context.Set<TillerSelectedSpreadsheetEntity>().Should().ContainSingle();
	}
}
