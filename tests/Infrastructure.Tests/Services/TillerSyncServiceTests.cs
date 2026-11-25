using Application.Interfaces.Services;
using Application.Models.Tiller;
using FluentAssertions;
using Infrastructure.Entities.Core;
using Infrastructure.Services;
using Infrastructure.Tests.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Infrastructure.Tests.Services;

// Slice 3 (TDD RED): native read-only sync engine. Reads ONLY through the
// allowlisted client; there is no write path to Tiller anywhere here.
// Identity is the provider transactionId; row numbers are never IDs.
public class TillerSyncServiceTests
{
	private readonly IDbContextFactory<ApplicationDbContext> _factory = DbContextHelpers.CreateInMemoryContextFactory();
	private readonly Mock<ITillerApiClient> _clientMock = new();
	private readonly Mock<ITillerSelectionService> _selectionMock = new();

	private const string Sheet = "sheet-1";
	private readonly List<TillerTransactionRow> _rows = [];

	private TillerSyncService CreateService(int pageSize = 100)
	{
		_selectionMock.Setup(s => s.GetSelectedSpreadsheetIdAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(Sheet);
		_clientMock.Setup(c => c.QueryTransactionsAsync(
				It.IsAny<string>(), It.IsAny<TillerTransactionQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((string spreadsheetId, TillerTransactionQuery query, CancellationToken _) =>
				Page(_rows, query));
		return new TillerSyncService(
			_factory, _clientMock.Object, _selectionMock.Object,
			new Tiller.TillerClientOptions { DefaultPageSize = pageSize, MaxPageSize = 500 });
	}

	private static TillerTransactionPage Page(List<TillerTransactionRow> rows, TillerTransactionQuery query)
	{
		IEnumerable<TillerTransactionRow> ordered = rows.OrderBy(r => r.Date).ThenBy(r => r.TransactionId);
		if (query.DateFrom.HasValue)
		{
			ordered = ordered.Where(r => r.Date >= query.DateFrom.Value);
		}

		List<TillerTransactionRow> page = ordered.Skip(query.Offset).Take(query.Limit).ToList();
		bool truncated = query.Offset + page.Count < ordered.Count();
		return new TillerTransactionPage(page, truncated, ordered.Count());
	}

	private static TillerTransactionRow Row(
		string id, string date, string description, decimal amount,
		string status = "posted", string category = "Groceries",
		string account = "Chase Checking", string? splitGroupId = null) => new(
		id, DateOnly.Parse(date), description, amount, category, account, "Chase", status, splitGroupId);

	private async Task<int> CanonicalCountAsync()
	{
		await using ApplicationDbContext context = _factory.CreateDbContext();
		return await context.Set<TillerTransactionEntity>().CountAsync(e => e.SupersededBy == null);
	}

	[Fact]
	public async Task SyncFull_PagesMultiPageHistory()
	{
		// Arrange
		_rows.AddRange([
			Row("t-1", "2026-10-01", "WHOLEFDS MKT", -9.99m),
			Row("t-2", "2026-10-05", "TRADER JOES", -24.50m),
			Row("t-3", "2026-11-02", "WHOLEFDS MKT", -12.00m),
			Row("t-4", "2026-11-03", "COSTCO", -60.00m),
			Row("t-5", "2026-11-04", "SHELL", -40.00m),
		]);

		// Act
		TillerSyncResult result = await CreateService(pageSize: 2).SyncFullAsync(CancellationToken.None);

		// Assert
		result.Inserted.Should().Be(5);
		result.Pages.Should().Be(3);
		(await CanonicalCountAsync()).Should().Be(5);
		_clientMock.Verify(c => c.QueryTransactionsAsync(
			Sheet, It.IsAny<TillerTransactionQuery>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
	}

	[Fact]
	public async Task SyncIncremental_AddsOnlyNewRowsAndConverges()
	{
		// Arrange
		_rows.Add(Row("t-1", "2026-10-01", "WHOLEFDS MKT", -9.99m));
		TillerSyncService service = CreateService();
		await service.SyncFullAsync(CancellationToken.None);
		_rows.Add(Row("t-2", "2026-11-05", "TRADER JOES", -24.50m));

		// Act
		TillerSyncResult first = await service.SyncIncrementalAsync(CancellationToken.None);
		TillerSyncResult second = await service.SyncIncrementalAsync(CancellationToken.None);

		// Assert
		first.Inserted.Should().Be(1);
		second.Inserted.Should().Be(0);
		(await CanonicalCountAsync()).Should().Be(2);
	}

	[Fact]
	public async Task PendingAndPosted_DistinctSourceIdsAreNotMerged()
	{
		// Arrange
		_rows.Add(Row("p-1", "2026-11-06", "WHOLEFDS MKT", -15.00m, status: "pending"));
		TillerSyncService service = CreateService();
		await service.SyncFullAsync(CancellationToken.None);
		_rows.Add(Row("t-9", "2026-11-06", "WHOLEFDS MKT", -15.00m, status: "posted"));

		// Act
		TillerSyncResult result = await service.SyncIncrementalAsync(CancellationToken.None);

		// Assert
		result.Reconciled.Should().Be(0);
		result.Inserted.Should().Be(1);
		(await CanonicalCountAsync()).Should().Be(2);
		await using ApplicationDbContext context = _factory.CreateDbContext();
		(await context.Set<TillerTransactionEntity>().ToListAsync())
			.Select(e => e.ProviderTransactionId).Should().BeEquivalentTo("p-1", "t-9");
	}

	[Fact]
	public async Task IdenticalPurchaseFacts_DistinctProviderIdsRemainDistinct()
	{
		// Arrange
		_rows.Add(Row("t-1", "2026-10-01", "WHOLEFDS MKT", -9.99m));
		TillerSyncService service = CreateService();
		await service.SyncFullAsync(CancellationToken.None);
		_rows.Add(Row("t-1b", "2026-10-01", "WHOLEFDS MKT", -9.99m));

		// Act
		TillerSyncResult result = await service.SyncIncrementalAsync(CancellationToken.None);

		// Assert
		result.Inserted.Should().Be(1);
		(await CanonicalCountAsync()).Should().Be(2);
		// A repeat read of the same IDs must stay converged.
		TillerSyncResult again = await service.SyncIncrementalAsync(CancellationToken.None);
		again.Inserted.Should().Be(0);
		(await CanonicalCountAsync()).Should().Be(2);
	}

	[Fact]
	public async Task FullReconciliation_UpdatesOlderTransactions()
	{
		_rows.Add(Row("old", "2026-10-01", "STORE", -9.99m));
		_rows.Add(Row("new", "2026-11-05", "STORE", -24.50m));
		TillerSyncService service = CreateService();
		await service.SyncFullAsync(CancellationToken.None);
		_rows[0] = _rows[0] with { Category = "Dining" };

		TillerSyncResult result = await service.SyncFullAsync(CancellationToken.None);

		result.Updated.Should().Be(1);
		result.Inserted.Should().Be(0);
		(await CanonicalCountAsync()).Should().Be(2);
		await using ApplicationDbContext context = _factory.CreateDbContext();
		(await context.Set<TillerTransactionEntity>().SingleAsync(e => e.ProviderTransactionId == "old"))
			.Category.Should().Be("Dining");
	}

	[Fact]
	public async Task FullReconciliation_MissingSourceIdHoldsWithoutChangingCache()
	{
		_rows.Add(Row("old", "2026-10-01", "STORE", -9.99m));
		TillerSyncService service = CreateService();
		await service.SyncFullAsync(CancellationToken.None);
		_rows.Clear();
		_rows.Add(Row("new", "2026-11-05", "STORE", -24.50m));

		Func<Task> act = () => service.SyncFullAsync(CancellationToken.None);
		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*omitted 1 cached transaction IDs*");
		await using ApplicationDbContext context = _factory.CreateDbContext();
		(await context.Set<TillerTransactionEntity>().SingleAsync()).ProviderTransactionId.Should().Be("old");
	}

	[Fact]
	public async Task CategoryEdit_RevisesInPlaceAndPreservesLinks()
	{
		// Arrange
		Guid receiptId = Guid.NewGuid();
		_rows.Add(Row("t-1", "2026-10-01", "WHOLEFDS MKT", -9.99m));
		TillerSyncService service = CreateService();
		await service.SyncFullAsync(CancellationToken.None);
		await using (ApplicationDbContext context = _factory.CreateDbContext())
		{
			context.Set<TillerReceiptLinkEntity>().Add(new TillerReceiptLinkEntity
			{
				Id = Guid.NewGuid(),
				ReceiptId = receiptId,
				ProviderTransactionId = "t-1",
				LinkKind = "manual",
				CreatedAt = DateTimeOffset.UtcNow,
			});
			await context.SaveChangesAsync();
		}

		_rows[0] = _rows[0] with { Category = "Dining" };

		// Act
		TillerSyncResult result = await service.SyncIncrementalAsync(CancellationToken.None);

		// Assert
		result.Updated.Should().Be(1);
		await using ApplicationDbContext verify = _factory.CreateDbContext();
		TillerTransactionEntity rec = await verify.Set<TillerTransactionEntity>().SingleAsync();
		rec.Revision.Should().Be(2);
		rec.Category.Should().Be("Dining");
		verify.Set<TillerReceiptLinkEntity>().Should().ContainSingle()
			.Which.ReceiptId.Should().Be(receiptId);
	}

	[Fact]
	public async Task CrashMidBackfill_ResumesAndCompletesWithoutDuplicates()
	{
		// Arrange
		_rows.AddRange([
			Row("t-1", "2026-10-01", "A", -1m),
			Row("t-2", "2026-10-02", "B", -2m),
			Row("t-3", "2026-10-03", "C", -3m),
			Row("t-4", "2026-10-04", "D", -4m),
		]);
		int calls = 0;
		_clientMock.Setup(c => c.QueryTransactionsAsync(
				It.IsAny<string>(), It.IsAny<TillerTransactionQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((string _, TillerTransactionQuery query, CancellationToken _) =>
			{
				calls++;
				if (calls > 2)
				{
					throw new HttpRequestException("simulated transport failure mid-backfill");
				}

				return Page(_rows, query);
			});
		_selectionMock.Setup(s => s.GetSelectedSpreadsheetIdAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(Sheet);
		TillerSyncService service = new(
			_factory, _clientMock.Object, _selectionMock.Object,
			new Tiller.TillerClientOptions { DefaultPageSize = 1, MaxPageSize = 500 });

		// Act: failed source scan writes no transaction rows; retry completes.
		Func<Task> failing = () => service.SyncFullAsync(CancellationToken.None);
		await failing.Should().ThrowAsync<HttpRequestException>();
		(await CanonicalCountAsync()).Should().Be(0);
		_clientMock.Setup(c => c.QueryTransactionsAsync(
				It.IsAny<string>(), It.IsAny<TillerTransactionQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((string _, TillerTransactionQuery query, CancellationToken _) =>
				Page(_rows, query));
		TillerSyncResult retry = await service.SyncFullAsync(CancellationToken.None);

		// Assert
		(await CanonicalCountAsync()).Should().Be(4);
		retry.Inserted.Should().BeGreaterThan(0);
	}

	[Fact]
	public async Task SplitsAndRefunds_AreStoredFaithfully()
	{
		// Arrange
		_rows.AddRange([
			Row("t-1", "2026-10-01", "COSTCO", -40.00m, splitGroupId: "s-1"),
			Row("t-2", "2026-10-01", "COSTCO", -20.00m, splitGroupId: "s-1"),
			Row("t-3", "2026-10-02", "REFUND WHOLEFDS", 9.99m),
		]);

		// Act
		await CreateService().SyncFullAsync(CancellationToken.None);

		// Assert
		await using ApplicationDbContext context = _factory.CreateDbContext();
		List<TillerTransactionEntity> all = await context.Set<TillerTransactionEntity>().ToListAsync();
		all.Should().HaveCount(3);
		all.Count(e => e.SplitGroupId == "s-1").Should().Be(2);
		all.Should().Contain(e => e.Amount == 9.99m);
	}

	[Fact]
	public async Task PlaceholderRowIds_AreSkippedAndNeverStored()
	{
		// Arrange
		_rows.AddRange([
			Row("row-4", "2026-10-01", "WHOLEFDS MKT", -9.99m),
			Row("t-1", "2026-10-01", "TRADER JOES", -24.50m),
		]);

		// Act
		TillerSyncResult result = await CreateService().SyncFullAsync(CancellationToken.None);

		// Assert
		result.Inserted.Should().Be(1);
		result.Invalid.Should().Be(1);
		(await CanonicalCountAsync()).Should().Be(1);
	}

	[Fact]
	public async Task SyncWithoutSelection_ThrowsBeforeAnyTransport()
	{
		// Arrange
		_selectionMock.Setup(s => s.GetSelectedSpreadsheetIdAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((string?)null);
		TillerSyncService service = new(_factory, _clientMock.Object, _selectionMock.Object);

		// Act
		Func<Task> act = () => service.SyncFullAsync(CancellationToken.None);

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No Tiller spreadsheet selected*");
		_clientMock.Verify(c => c.QueryTransactionsAsync(
			It.IsAny<string>(), It.IsAny<TillerTransactionQuery>(), It.IsAny<CancellationToken>()), Times.Never);
	}
}
