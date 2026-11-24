using Application.Interfaces.Services;
using Application.Models.Tiller;
using FluentAssertions;
using Infrastructure.Entities.Core;
using Infrastructure.Services;
using Infrastructure.Tests.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Infrastructure.Tests.Services;

// TDD RED: pagination guards mirroring the verified Tiller-read pattern
// (total-consistency, exact pages, repeat detection, fail-closed missing total).
// Reads only; no writes to any financial source.
public class TillerSyncPaginationGuardTests
{
	private readonly IDbContextFactory<ApplicationDbContext> _factory = DbContextHelpers.CreateInMemoryContextFactory();
	private readonly Mock<ITillerApiClient> _clientMock = new(MockBehavior.Strict);
	private readonly Mock<ITillerSelectionService> _selectionMock = new();

	private const string Sheet = "sheet-1";

	private static TillerTransactionRow Row(string id, string date) => new(
		id, DateOnly.Parse(date), "STORE", -10.00m, "Groceries", "Checking", "Chase", "posted", null);

	private TillerSyncService CreateService(Queue<TillerTransactionPage> pages, int pageSize = 2)
	{
		_selectionMock.Setup(s => s.GetSelectedSpreadsheetIdAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(Sheet);
		_clientMock.Setup(c => c.QueryTransactionsAsync(
				It.IsAny<string>(), It.IsAny<TillerTransactionQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((string _, TillerTransactionQuery _, CancellationToken _) => pages.Dequeue());
		return new TillerSyncService(
			_factory, _clientMock.Object, _selectionMock.Object,
			new Tiller.TillerClientOptions { DefaultPageSize = pageSize, MaxPageSize = 500 });
	}

	private static TillerTransactionPage Page(IReadOnlyList<TillerTransactionRow> rows, bool truncated, int? total) =>
		new(rows, truncated, total);

	[Fact]
	public async Task SyncFull_TotalChangesMidRun_Throws()
	{
		var pages = new Queue<TillerTransactionPage>([
			Page([Row("t-1", "2026-10-01"), Row("t-2", "2026-10-02")], true, 3),
			Page([Row("t-3", "2026-10-03")], false, 4),
		]);

		Func<Task> act = () => CreateService(pages).SyncFullAsync(CancellationToken.None);

		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("*dataset changed*");
	}

	[Fact]
	public async Task SyncFull_ShortNonFinalPage_Throws()
	{
		var pages = new Queue<TillerTransactionPage>([
			Page([Row("t-1", "2026-10-01")], true, 3),
		]);

		Func<Task> act = () => CreateService(pages).SyncFullAsync(CancellationToken.None);

		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("*ncomplete*");
	}

	[Fact]
	public async Task SyncFull_RepeatedPage_Throws()
	{
		var page = Page([Row("t-1", "2026-10-01"), Row("t-2", "2026-10-02")], true, 4);
		var pages = new Queue<TillerTransactionPage>([page, page]);

		Func<Task> act = () => CreateService(pages).SyncFullAsync(CancellationToken.None);

		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("*epeat*");
	}

	[Fact]
	public async Task SyncFull_MissingTotal_Throws()
	{
		var pages = new Queue<TillerTransactionPage>([
			Page([Row("t-1", "2026-10-01")], false, null),
		]);

		Func<Task> act = () => CreateService(pages).SyncFullAsync(CancellationToken.None);

		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("*count*");
	}
}
