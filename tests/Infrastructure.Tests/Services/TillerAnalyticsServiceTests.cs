using Application.Interfaces.Services;
using Application.Models.Tiller;
using FluentAssertions;
using Infrastructure.Entities.Core;
using Infrastructure.Services;
using Infrastructure.Tests.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Infrastructure.Tests.Services;

// Analytics RED: product facts come ONLY from verified receipt lines;
// unmatched bank rows are neutral context; bank vs captured buckets stay
// separate with an explicit scope note.
public class TillerAnalyticsServiceTests
{
	private readonly IDbContextFactory<ApplicationDbContext> _factory = DbContextHelpers.CreateInMemoryContextFactory();
	private readonly Mock<ITillerReceiptCatalog> _catalogMock = new();

	private TillerAnalyticsService CreateService(
		List<TillerReceiptLine>? lines = null,
		List<TillerReceiptSnapshot>? snapshots = null)
	{
		_catalogMock.Setup(c => c.GetVerifiedReceiptLinesAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((CancellationToken _) => (IReadOnlyList<TillerReceiptLine>)(lines ?? []));
		_catalogMock.Setup(c => c.GetReceiptSnapshotsAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((CancellationToken _) => (IReadOnlyList<TillerReceiptSnapshot>)(snapshots ?? []));
		return new TillerAnalyticsService(_factory, _catalogMock.Object);
	}

	private static TillerReceiptLine Line(
		Guid receiptId, string merchant, string date, string description,
		decimal quantity, decimal unitPrice, decimal total) =>
		new(receiptId, merchant, DateOnly.Parse(date), description, quantity, unitPrice, total);

	private async Task AddTxnAsync(string providerId, string date, string description, decimal amount)
	{
		await using ApplicationDbContext context = _factory.CreateDbContext();
		DateTimeOffset now = DateTimeOffset.UtcNow;
		context.Set<TillerTransactionEntity>().Add(new TillerTransactionEntity
		{
			Id = Guid.NewGuid(),
			ProviderTransactionId = providerId,
			SpreadsheetId = "sheet-1",
			Date = DateOnly.Parse(date),
			Description = description,
			Amount = amount,
			Category = "Groceries",
			Account = "Chase Checking",
			Institution = "Chase",
			Status = "posted",
			Revision = 1,
			CreatedAt = now,
			UpdatedAt = now,
		});
		await context.SaveChangesAsync();
	}

	private async Task AddLinkAsync(Guid receiptId, string providerId, string kind = "auto")
	{
		await using ApplicationDbContext context = _factory.CreateDbContext();
		context.Set<TillerReceiptLinkEntity>().Add(new TillerReceiptLinkEntity
		{
			Id = Guid.NewGuid(),
			ReceiptId = receiptId,
			ProviderTransactionId = providerId,
			LinkKind = kind,
			CreatedAt = DateTimeOffset.UtcNow,
		});
		await context.SaveChangesAsync();
	}

	[Fact]
	public async Task UnitPricesAndBaskets_ComeFromVerifiedLinesOnly()
	{
		// Arrange
		Guid receiptId = Guid.NewGuid();
		var lines = new List<TillerReceiptLine>
		{
			Line(receiptId, "Whole Foods", "2026-11-01", "Organic Bananas", 2m, 0.99m, 1.98m),
			Line(receiptId, "Whole Foods", "2026-11-01", "Milk", 1m, 4.49m, 4.49m),
		};
		var snapshots = new List<TillerReceiptSnapshot>
		{
			new(receiptId, "Whole Foods", new DateOnly(2026, 11, 1), 6.47m, true),
		};
		await AddTxnAsync("t-1", "2026-11-01", "WHOLEFDS MKT", -6.47m);
		await AddLinkAsync(receiptId, "t-1");

		// Act
		TillerAnalyticsResult result = await CreateService(lines, snapshots).GetAnalyticsAsync(CancellationToken.None);

		// Assert
		result.Products.Should().HaveCount(2);
		result.Products.Should().Contain(p => p.Description == "Organic Bananas" && p.UnitPrice == 0.99m);
		result.Baskets.Should().ContainSingle().Which.Payment!.ProviderTransactionId.Should().Be("t-1");
		result.Baskets[0].Payment!.Account.Should().Be("Chase Checking");
	}

	[Fact]
	public async Task UnmatchedTransactions_AreNeutralContext()
	{
		// Arrange
		await AddTxnAsync("t-1", "2026-11-01", "SHELL", -40.00m);

		// Act
		TillerAnalyticsResult result = await CreateService().GetAnalyticsAsync(CancellationToken.None);

		// Assert
		result.UnmatchedTransactions.Should().ContainSingle().Which.ProviderTransactionId.Should().Be("t-1");
		result.Alerts.Should().BeEmpty();
		result.CapturedReceiptCount.Should().Be(0);
	}

	[Fact]
	public async Task BankSpendingAndCapturedReceipts_AreNeverMixed()
	{
		// Arrange
		Guid receiptId = Guid.NewGuid();
		var lines = new List<TillerReceiptLine>
		{
			Line(receiptId, "Whole Foods", "2026-11-01", "Milk", 1m, 4.49m, 4.49m),
		};
		var snapshots = new List<TillerReceiptSnapshot>
		{
			new(receiptId, "Whole Foods", new DateOnly(2026, 11, 1), 4.49m, true),
		};
		await AddTxnAsync("t-1", "2026-11-01", "WHOLEFDS MKT", -4.49m);
		await AddTxnAsync("t-2", "2026-11-02", "SHELL", -40.00m);
		await AddLinkAsync(receiptId, "t-1");

		// Act
		TillerAnalyticsResult result = await CreateService(lines, snapshots).GetAnalyticsAsync(CancellationToken.None);

		// Assert
		result.BankSpendingTotal.Should().Be(-44.49m);
		result.CapturedReceiptsTotal.Should().Be(4.49m);
		result.CapturedReceiptsNote.Should().Contain("Captured and reviewed receipts only");
	}

	[Fact]
	public async Task NoProducts_AreInventedFromTransactionOnlyData()
	{
		// Arrange
		await AddTxnAsync("t-1", "2026-11-01", "WHOLEFDS MKT FRESH PRODUCE", -22.10m);

		// Act
		TillerAnalyticsResult result = await CreateService().GetAnalyticsAsync(CancellationToken.None);

		// Assert
		result.Products.Should().BeEmpty();
		result.Stores.Should().BeEmpty();
	}

	[Fact]
	public async Task StoreRollups_GroupVerifiedLinesByMerchant()
	{
		// Arrange
		var lines = new List<TillerReceiptLine>
		{
			Line(Guid.NewGuid(), "Whole Foods", "2026-11-01", "Milk", 1m, 4.49m, 4.49m),
			Line(Guid.NewGuid(), "Whole Foods", "2026-11-08", "Eggs", 1m, 5.99m, 5.99m),
			Line(Guid.NewGuid(), "Costco", "2026-11-02", "Paper Towels", 1m, 19.99m, 19.99m),
		};

		// Act
		TillerAnalyticsResult result = await CreateService(lines).GetAnalyticsAsync(CancellationToken.None);

		// Assert
		result.Stores.Should().Contain(s => s.Store == "Whole Foods" && s.ReceiptCount == 2 && s.Total == 10.48m);
	}
}
