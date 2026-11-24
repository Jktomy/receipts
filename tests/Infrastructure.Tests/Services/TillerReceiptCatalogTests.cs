using Application.Models.Tiller;
using FluentAssertions;
using Infrastructure.Entities.Core;
using Infrastructure.Services;
using Infrastructure.Tests.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Services;

// Catalog contract: totals come from stored line-item amounts; receipts with
// no items are invisible to Tiller; unreviewed store-default rows report
// AllItemsVerified=false (review state is owned by the normalization flows,
// and raw inserts are never treated as human-verified).
public class TillerReceiptCatalogTests
{
	private readonly IDbContextFactory<ApplicationDbContext> _factory = DbContextHelpers.CreateInMemoryContextFactory();

	[Fact]
	public async Task GetReceiptSnapshots_ReportsTotalsAndExcludesItemlessReceipts()
	{
		// Arrange
		Guid withItems = Guid.NewGuid();
		Guid itemless = Guid.NewGuid();
		await using (ApplicationDbContext context = _factory.CreateDbContext())
		{
			context.Set<ReceiptEntity>().Add(new ReceiptEntity
			{
				Id = withItems,
				NormalizationRevision = Guid.NewGuid(),
				Location = "Costco",
				Date = new DateOnly(2026, 11, 2),
				TaxAmount = 0m,
			});
			context.Set<ReceiptEntity>().Add(new ReceiptEntity
			{
				Id = itemless,
				NormalizationRevision = Guid.NewGuid(),
				Location = "Empty",
				Date = new DateOnly(2026, 11, 2),
				TaxAmount = 0m,
			});
			context.Set<ReceiptItemEntity>().Add(new ReceiptItemEntity
			{
				Id = Guid.NewGuid(),
				ReceiptId = withItems,
				NormalizationRevision = Guid.NewGuid(),
				Description = "Paper Towels",
				Quantity = 1m,
				UnitPrice = 19.99m,
				TotalAmount = 19.99m,
				Category = "Household",
			});
			context.Set<ReceiptItemEntity>().Add(new ReceiptItemEntity
			{
				Id = Guid.NewGuid(),
				ReceiptId = withItems,
				NormalizationRevision = Guid.NewGuid(),
				Description = "Milk",
				Quantity = 1m,
				UnitPrice = 4.49m,
				TotalAmount = 4.49m,
				Category = "Groceries",
			});
			await context.SaveChangesAsync();
		}

		// Act
		IReadOnlyList<TillerReceiptSnapshot> snapshots =
			await new TillerReceiptCatalog(_factory).GetReceiptSnapshotsAsync(CancellationToken.None);

		// Assert
		snapshots.Should().ContainSingle().Which.Should().BeEquivalentTo(
			new TillerReceiptSnapshot(withItems, "Costco", new DateOnly(2026, 11, 2), 24.48m, false));
	}

	[Fact]
	public async Task GetReceiptSnapshot_MissingId_ReturnsNull()
	{
		// Act
		TillerReceiptSnapshot? snapshot =
			await new TillerReceiptCatalog(_factory).GetReceiptSnapshotAsync(Guid.NewGuid(), CancellationToken.None);

		// Assert
		snapshot.Should().BeNull();
	}
}
