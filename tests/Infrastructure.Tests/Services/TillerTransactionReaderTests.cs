using Application.Interfaces.Services;
using FluentAssertions;
using Infrastructure.Entities.Core;
using Infrastructure.Services;
using Infrastructure.Tests.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Services;

// Reader RED: paged/filtered local cache reads with link overlay; invalid
// paging/sort rejected before any query.
public class TillerTransactionReaderTests
{
	private readonly IDbContextFactory<ApplicationDbContext> _factory = DbContextHelpers.CreateInMemoryContextFactory();

	private TillerTransactionReader CreateReader() => new(_factory);

	private async Task SeedTxnsAsync()
	{
		await using ApplicationDbContext context = _factory.CreateDbContext();
		DateTimeOffset now = DateTimeOffset.UtcNow;
		TillerTransactionEntity t1 = new()
		{
			Id = Guid.NewGuid(),
			ProviderTransactionId = "t-1",
			SpreadsheetId = "sheet-1",
			Date = new DateOnly(2026, 11, 1),
			Description = "WHOLEFDS MKT",
			Amount = -9.99m,
			Category = "Groceries",
			Account = "Chase Checking",
			Institution = "Chase",
			Status = "posted",
			Revision = 1,
			CreatedAt = now,
			UpdatedAt = now,
		};
		TillerTransactionEntity t2 = new()
		{
			Id = Guid.NewGuid(),
			ProviderTransactionId = "t-2",
			SpreadsheetId = "sheet-1",
			Date = new DateOnly(2026, 11, 5),
			Description = "SHELL",
			Amount = -40.00m,
			Category = "Gas",
			Account = "Savings",
			Institution = "Chase",
			Status = "posted",
			Revision = 1,
			CreatedAt = now,
			UpdatedAt = now,
		};
		context.Set<TillerTransactionEntity>().AddRange(t1, t2);
		Guid receiptId = Guid.NewGuid();
		context.Set<TillerReceiptLinkEntity>().Add(new TillerReceiptLinkEntity
		{
			Id = Guid.NewGuid(),
			ReceiptId = receiptId,
			ProviderTransactionId = "t-1",
			LinkKind = "auto",
			CreatedAt = now,
		});
		await context.SaveChangesAsync();
	}

	[Fact]
	public async Task GetTransactions_PagesWithLinkOverlay()
	{
		// Arrange
		await SeedTxnsAsync();

		// Act
		TillerTransactionListResult page = await CreateReader().GetTransactionsAsync(
			0, 50, null, null, null, unlinkedOnly: false, account: null, CancellationToken.None);

		// Assert
		page.Total.Should().Be(2);
		page.Rows.Should().HaveCount(2);
		page.Rows.Should().Contain(r => r.ProviderTransactionId == "t-1" && r.LinkKind == "auto");
		page.Rows.Should().Contain(r => r.ProviderTransactionId == "t-2" && r.LinkKind == null);
	}

	[Fact]
	public async Task GetTransactions_UnlinkedOnly_ExcludesLinked()
	{
		// Arrange
		await SeedTxnsAsync();

		// Act
		TillerTransactionListResult page = await CreateReader().GetTransactionsAsync(
			0, 50, null, null, null, unlinkedOnly: true, account: null, CancellationToken.None);

		// Assert
		page.Rows.Should().ContainSingle().Which.ProviderTransactionId.Should().Be("t-2");
	}

	[Fact]
	public async Task GetTransactions_InvalidPaging_ThrowsBeforeQuery()
	{
		// Act
		Func<Task> negative = () => CreateReader().GetTransactionsAsync(
			-1, 50, null, null, null, false, null, CancellationToken.None);
		Func<Task> tooMany = () => CreateReader().GetTransactionsAsync(
			0, 501, null, null, null, false, null, CancellationToken.None);
		Func<Task> badSort = () => CreateReader().GetTransactionsAsync(
			0, 50, "bogus", null, null, false, null, CancellationToken.None);

		// Assert
		await negative.Should().ThrowAsync<ArgumentException>();
		await tooMany.Should().ThrowAsync<ArgumentException>();
		await badSort.Should().ThrowAsync<ArgumentException>();
	}

	[Fact]
	public async Task GetSyncEvents_PagesAndFiltersByOutcome()
	{
		// Arrange
		await using (ApplicationDbContext context = _factory.CreateDbContext())
		{
			DateTimeOffset now = DateTimeOffset.UtcNow;
			context.Set<TillerSyncEventEntity>().AddRange(
				new TillerSyncEventEntity
				{
					Id = Guid.NewGuid(),
					SpreadsheetId = "sheet-1",
					EventType = "Full",
					Success = true,
					Inserted = 5,
					OccurredAt = now.AddMinutes(-2),
				},
				new TillerSyncEventEntity
				{
					Id = Guid.NewGuid(),
					SpreadsheetId = "sheet-1",
					EventType = "Incremental",
					Success = false,
					ErrorMessage = "boom",
					OccurredAt = now,
				});
			await context.SaveChangesAsync();
		}

		// Act
		TillerSyncEventListResult all = await CreateReader().GetSyncEventsAsync(0, 50, null, CancellationToken.None);
		TillerSyncEventListResult failures = await CreateReader().GetSyncEventsAsync(0, 50, false, CancellationToken.None);

		// Assert
		all.Total.Should().Be(2);
		all.Rows.First().Success.Should().BeFalse("most recent first");
		failures.Rows.Should().ContainSingle().Which.ErrorMessage.Should().Be("boom");
	}
}
