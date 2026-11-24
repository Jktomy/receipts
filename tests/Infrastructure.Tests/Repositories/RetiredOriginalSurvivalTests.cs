using FluentAssertions;
using Infrastructure.Entities.Core;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using SampleData.Entities;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// A retired Paperless original must not take the persisted specialist record
/// with it. These tests use the repository InMemory harness with NO Paperless
/// dependency at all (no fetcher, no network): reads, edits and list/analytics
/// projections operate on app-owned rows only. Import-time denial of retired
/// sources is proven separately at the bridge boundary; replay de-duplication
/// at the persistence boundary is proven by ReplayAtomicityTests on real
/// PostgreSQL (filtered unique index on SourcePaperlessDocumentId).
/// </summary>
public class RetiredOriginalSurvivalTests
{
	private readonly IDbContextFactory<ApplicationDbContext> _contextFactory = DbContextHelpers.CreateInMemoryContextFactory();

	[Fact]
	public async Task PersistedPurchaseWithPaperlessSource_SurvivesRetirement_ReadEditAndProjectionsWork()
	{
		// Arrange: an imported purchase whose original may later retire.
		ReceiptEntity receipt = ReceiptEntityGenerator.Generate();
		receipt.SourcePaperlessDocumentId = 44;
		receipt.Location = "H-E-B";
		ReceiptRepository repository = new(_contextFactory);
		await repository.CreateAsync([receipt], CancellationToken.None);

		ReceiptItemEntity item = ReceiptItemEntityGenerator.Generate(receipt.Id);
		item.Description = "Milk";
		item.TotalAmount = 3.99m;
		using (ApplicationDbContext context = _contextFactory.CreateDbContext())
		{
			await context.Set<ReceiptItemEntity>().AddAsync(item);
			await context.SaveChangesAsync(CancellationToken.None);
		}

		// Act: the original retires. No availability check exists or is needed:
		// read, edit, and projection paths take no source parameter.
		ReceiptEntity? reread = await repository.GetByIdAsync(receipt.Id, CancellationToken.None);
		Application.Models.PagedResult<Application.Models.ReceiptListItem> page =
			await repository.GetAsync(0, 10, new Application.Models.SortParams("date", "desc"), null, null, null, null, CancellationToken.None);

		// Assert: provenance + structured record intact.
		reread.Should().NotBeNull();
		reread!.SourcePaperlessDocumentId.Should().Be(44);
		reread.Location.Should().Be("H-E-B");
		page.Data.Should().ContainSingle(x => x.Id == receipt.Id);

		// Act: reviewer correction after retirement.
		reread.Location = "H-E-B Plus";
		await repository.UpdateAsync([reread], CancellationToken.None);

		// Assert: edit persisted, provenance untouched.
		using ApplicationDbContext verify = _contextFactory.CreateDbContext();
		ReceiptEntity edited = await verify.Receipts.SingleAsync(x => x.Id == receipt.Id);
		edited.Location.Should().Be("H-E-B Plus");
		edited.SourcePaperlessDocumentId.Should().Be(44, "an edit must not disturb source provenance");
		(await verify.Set<ReceiptItemEntity>().CountAsync(x => x.ReceiptId == receipt.Id)).Should().Be(1);

		_contextFactory.ResetDatabase();
	}
}
