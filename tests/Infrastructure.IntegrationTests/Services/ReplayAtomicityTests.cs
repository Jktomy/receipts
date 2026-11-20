using Application.Interfaces.Services;
using Application.Normalization;
using Application.Taxonomy;
using Common;
using Domain;
using Domain.Core;
using FluentAssertions;
using Infrastructure.Entities.Core;
using Infrastructure.IntegrationTests.Fixtures;
using Infrastructure.Mapping;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.IntegrationTests.Services;

/// <summary>
/// ReplayAtomicityTests proves a replayed Paperless import with a different clientRequestId
/// resolves to the original receipt instead of throwing or duplicating rows.
/// Requires the 121 filtered unique index on Receipts.SourcePaperlessDocumentId.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Prerequisite", "Postgres")]
public sealed class ReplayAtomicityTests(PostgresFixture server) : IsolatedPostgresTest(server)
{
	private sealed class Actor : ICurrentUserAccessor
	{
		public string? UserId => "replay-probe";
		public Guid? ApiKeyId => null;
		public string? IpAddress => null;
		public string? UserAgent => null;
	}

	private sealed class Factory(PostgresFixture database) : IDbContextFactory<ApplicationDbContext>
	{
		public ApplicationDbContext CreateDbContext() => new(database.CreateOptions(), new Actor());
	}

	private CompleteReceiptWriter Writer() => new(new Factory(fixture),
		new ReceiptMapper(), new TransactionMapper(), new ReceiptItemMapper(), new AdjustmentMapper(),
		Options.Create(new ClassificationOptions { WritesEnabled = false }));

	private static Domain.Core.Receipt Draft(long sourceDocumentId) =>
		new(Guid.NewGuid(), "Replay store", new DateOnly(2026, 1, 5), new Money(1.00m))
		{ SourcePaperlessDocumentId = sourceDocumentId };

	private static Domain.Core.ReceiptItem DraftItem() =>
		new(Guid.NewGuid(), null, "Probe item", 1, new Money(1.00m), new Money(1.00m), "Groceries", null);

	[Fact]
	public async Task ReplayWithDifferentRequestId_ReturnsOriginalReceiptWithOneRow()
	{
		const long sourceId = 900001;
		var first = await Writer().CreateAsync(Draft(sourceId), [], [DraftItem()], [], Guid.NewGuid(), CancellationToken.None);
		var replay = await Writer().CreateAsync(Draft(sourceId), [], [DraftItem()], [], Guid.NewGuid(), CancellationToken.None);

		replay.Receipt.Id.Should().Be(first.Receipt.Id);
		await using var db = fixture.CreateDbContext();
		(await db.Receipts.IgnoreQueryFilters().CountAsync(x => x.SourcePaperlessDocumentId == sourceId))
			.Should().Be(1, "the unique index must hold exactly one receipt per Paperless document");
		(await db.ReceiptWriteRequests.CountAsync(x => x.ReceiptId != Guid.Empty))
			.Should().Be(1, "a replay must not leave a second request row claiming another receipt");
	}

	[Fact]
	public async Task ConcurrentReplayWithDifferentRequestIds_ResolvesToSingleReceipt()
	{
		const long sourceId = 900002;
		var firstTask = Writer().CreateAsync(Draft(sourceId), [], [DraftItem()], [], Guid.NewGuid(), CancellationToken.None);
		var secondTask = Writer().CreateAsync(Draft(sourceId), [], [DraftItem()], [], Guid.NewGuid(), CancellationToken.None);
		var results = await Task.WhenAll(firstTask, secondTask);

		results[0].Receipt.Id.Should().Be(results[1].Receipt.Id);
		await using var db = fixture.CreateDbContext();
		(await db.Receipts.IgnoreQueryFilters().CountAsync(x => x.SourcePaperlessDocumentId == sourceId))
			.Should().Be(1, "concurrent replays must converge on one receipt");
	}

	[Fact]
	public async Task ReplayAgainstSoftDeletedWinner_ThrowsDistinctSourceDeletedCode()
	{
		const long sourceId = 900003;
		var first = await Writer().CreateAsync(Draft(sourceId), [], [DraftItem()], [], Guid.NewGuid(), CancellationToken.None);

		await using (var db = fixture.CreateDbContext())
		{
			var stored = await db.Receipts.IgnoreQueryFilters().SingleAsync(x => x.Id == first.Receipt.Id);
			stored.DeletedAt = DateTimeOffset.UtcNow;
			await db.SaveChangesAsync();
		}

		Func<Task> replay = () => Writer().CreateAsync(Draft(sourceId), [], [DraftItem()], [], Guid.NewGuid(), CancellationToken.None);
		var thrown = (await replay.Should().ThrowAsync<NormalizationCatalogException>()).Which;
		thrown.StatusCode.Should().Be(409);
		thrown.Code.Should().Be("receipt_source_deleted", "a soft-deleted source winner must not reuse the request-id collision code");
	}
}
