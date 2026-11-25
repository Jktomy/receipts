using Application.Exceptions;
using FluentAssertions;
using Infrastructure.Entities.Core;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using SampleData.Entities;

namespace Infrastructure.Tests.Repositories;

// Bind-once coverage for ReceiptRepository.UpdateAsync provenance handling:
// a receipt gains its Paperless source link at most once, a different id is
// rejected, and paperless-verified requires the link in the same update.
public class ReceiptProvenanceBindTests
{
	private readonly IDbContextFactory<ApplicationDbContext> _contextFactory = DbContextHelpers.CreateInMemoryContextFactory();

	private async Task<ReceiptEntity> SeedAsync(long? link, string? kind)
	{
		using ApplicationDbContext context = _contextFactory.CreateDbContext();
		ReceiptEntity entity = ReceiptEntityGenerator.Generate();
		entity.SourcePaperlessDocumentId = link;
		entity.SourceKind = kind;
		await context.Receipts.AddAsync(entity);
		await context.SaveChangesAsync(CancellationToken.None);
		context.ChangeTracker.Clear();
		return entity;
	}

	private static ReceiptEntity EditFor(Guid id, long? link, string? kind, ReceiptEntity template)
	{
		return new ReceiptEntity
		{
			Id = id,
			Location = template.Location,
			Date = template.Date,
			TaxAmount = template.TaxAmount,
			TaxAmountCurrency = template.TaxAmountCurrency,
			TaxUnknown = template.TaxUnknown,
			SourceKind = kind,
			SourcePaperlessDocumentId = link,
			PaidFromEnvelopeId = template.PaidFromEnvelopeId,
			OccasionId = template.OccasionId
		};
	}

	private async Task<ReceiptEntity> ReloadAsync(Guid id)
	{
		using ApplicationDbContext context = _contextFactory.CreateDbContext();
		return (await context.Receipts.SingleAsync(e => e.Id == id, CancellationToken.None))!;
	}

	[Fact]
	public async Task UpdateAsync_NullLink_BindsProvidedDocumentId()
	{
		ReceiptEntity seeded = await SeedAsync(null, null);
		ReceiptRepository repository = new(_contextFactory);

		await repository.UpdateAsync(
			[EditFor(seeded.Id, 336, "user-reported", seeded)], CancellationToken.None);

		(await ReloadAsync(seeded.Id)).SourcePaperlessDocumentId.Should().Be(336);
		_contextFactory.ResetDatabase();
	}

	[Fact]
	public async Task UpdateAsync_SameLink_IsIdempotentNoOp()
	{
		ReceiptEntity seeded = await SeedAsync(336, "paperless-verified");
		ReceiptRepository repository = new(_contextFactory);

		await repository.UpdateAsync(
			[EditFor(seeded.Id, 336, "paperless-verified", seeded)], CancellationToken.None);

		(await ReloadAsync(seeded.Id)).SourcePaperlessDocumentId.Should().Be(336);
		_contextFactory.ResetDatabase();
	}

	[Fact]
	public async Task UpdateAsync_DifferentLink_ThrowsProvenanceAssertion()
	{
		ReceiptEntity seeded = await SeedAsync(336, "paperless-verified");
		ReceiptRepository repository = new(_contextFactory);

		Func<Task> call = () => repository.UpdateAsync(
			[EditFor(seeded.Id, 321, "paperless-verified", seeded)], CancellationToken.None);

		await call.Should().ThrowAsync<ProvenanceAssertionException>()
			.WithMessage($"*{Domain.Core.Receipt.SourcePaperlessDocumentIdRebindRejected}*");
		(await ReloadAsync(seeded.Id)).SourcePaperlessDocumentId.Should().Be(336);
		_contextFactory.ResetDatabase();
	}

	[Fact]
	public async Task UpdateAsync_NullLinkEdit_PreservesStoredLink()
	{
		ReceiptEntity seeded = await SeedAsync(336, "paperless-verified");
		ReceiptRepository repository = new(_contextFactory);

		await repository.UpdateAsync(
			[EditFor(seeded.Id, null, "paperless-verified", seeded)], CancellationToken.None);

		(await ReloadAsync(seeded.Id)).SourcePaperlessDocumentId.Should().Be(336);
		_contextFactory.ResetDatabase();
	}

	[Fact]
	public async Task UpdateAsync_PaperlessVerifiedWithoutLink_Throws()
	{
		ReceiptEntity seeded = await SeedAsync(null, null);
		ReceiptRepository repository = new(_contextFactory);

		Func<Task> call = () => repository.UpdateAsync(
			[EditFor(seeded.Id, null, "paperless-verified", seeded)], CancellationToken.None);

		await call.Should().ThrowAsync<ProvenanceAssertionException>()
			.WithMessage($"*{Domain.Core.Receipt.PaperlessVerifiedRequiresDocumentId}*");
		_contextFactory.ResetDatabase();
	}

	[Fact]
	public async Task UpdateAsync_PaperlessVerifiedWithLinkInSameUpdate_Succeeds()
	{
		ReceiptEntity seeded = await SeedAsync(null, null);
		ReceiptRepository repository = new(_contextFactory);

		await repository.UpdateAsync(
			[EditFor(seeded.Id, 336, "paperless-verified", seeded)], CancellationToken.None);

		ReceiptEntity reloaded = await ReloadAsync(seeded.Id);
		reloaded.SourcePaperlessDocumentId.Should().Be(336);
		reloaded.SourceKind.Should().Be("paperless-verified");
		_contextFactory.ResetDatabase();
	}
}
