using Application.Interfaces.Services;
using Application.Models.Tiller;
using FluentAssertions;
using Infrastructure.Services;
using Infrastructure.Tests.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Services;

// Slice 2 (TDD RED): Tiller account-mapping persistence. Mirrors the YNAB
// mapping guards: mappings are bound to one spreadsheet and can never be
// moved between spreadsheets on update.
public class TillerAccountMappingServiceTests
{
	private readonly IDbContextFactory<ApplicationDbContext> _factory = DbContextHelpers.CreateInMemoryContextFactory();

	private TillerAccountMappingService CreateService() => new(_factory);

	[Fact]
	public async Task CreateAsync_PersistsMapping()
	{
		// Act
		TillerAccountMappingDto dto = await CreateService().CreateAsync(
			Guid.NewGuid(), "acc-x1", "Chase Checking", "sheet-1", CancellationToken.None);

		// Assert
		dto.Id.Should().NotBeEmpty();
		dto.TillerAccountId.Should().Be("acc-x1");
		dto.SpreadsheetId.Should().Be("sheet-1");
	}

	[Fact]
	public async Task GetBySpreadsheetId_ReturnsOnlyThatSpreadsheet()
	{
		// Arrange
		TillerAccountMappingService service = CreateService();
		await service.CreateAsync(Guid.NewGuid(), "acc-x1", "Chase Checking", "sheet-1", CancellationToken.None);
		await service.CreateAsync(Guid.NewGuid(), "acc-x2", "Savings", "sheet-9", CancellationToken.None);

		// Act
		List<TillerAccountMappingDto> result = await service.GetBySpreadsheetIdAsync("sheet-1", CancellationToken.None);

		// Assert
		result.Should().ContainSingle().Which.TillerAccountId.Should().Be("acc-x1");
	}

	[Fact]
	public async Task UpdateAsync_CannotMoveMappingToAnotherSpreadsheet()
	{
		// Arrange
		TillerAccountMappingService service = CreateService();
		TillerAccountMappingDto dto = await service.CreateAsync(
			Guid.NewGuid(), "acc-x1", "Chase Checking", "sheet-1", CancellationToken.None);

		// Act
		Func<Task> act = () => service.UpdateAsync(dto.Id, "acc-x2", "Savings", "sheet-2", CancellationToken.None);

		// Assert
		await act.Should().ThrowAsync<ArgumentException>().WithMessage("*cannot be moved between Tiller spreadsheets*");
		(await service.GetByIdAsync(dto.Id, CancellationToken.None))!.SpreadsheetId.Should().Be("sheet-1");
	}

	[Fact]
	public async Task UpdateAsync_MissingId_IsNoOp()
	{
		// Act
		Func<Task> act = () => CreateService().UpdateAsync(Guid.NewGuid(), "acc-x1", "X", "sheet-1", CancellationToken.None);

		// Assert
		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task DeleteAsync_RemovesMapping()
	{
		// Arrange
		TillerAccountMappingService service = CreateService();
		TillerAccountMappingDto dto = await service.CreateAsync(
			Guid.NewGuid(), "acc-x1", "Chase Checking", "sheet-1", CancellationToken.None);

		// Act
		await service.DeleteAsync(dto.Id, CancellationToken.None);

		// Assert
		(await service.GetByIdAsync(dto.Id, CancellationToken.None)).Should().BeNull();
	}

	[Fact]
	public async Task GetByIdAsync_MissingId_ReturnsNull()
	{
		// Act
		TillerAccountMappingDto? result = await CreateService().GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

		// Assert
		result.Should().BeNull();
	}
}
