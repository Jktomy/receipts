using Application.Interfaces.Services;
using Application.Models.Tiller;
using FluentAssertions;
using Infrastructure.Entities.Core;
using Infrastructure.Services;
using Infrastructure.Tests.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Infrastructure.Tests.Services;

// Slice 4 (TDD): deterministic receipt<->transaction matching. Receipt facts
// arrive through the mocked read-only receipt catalog (persistence of review
// state belongs to the normalization flows, not to this slice); Tiller rows
// supply payment context. Ambiguity never auto-links. Missing receipts are
// normal: unmatched rows report "none", never a flag.
public class TillerMatchingServiceTests
{
	private readonly IDbContextFactory<ApplicationDbContext> _factory = DbContextHelpers.CreateInMemoryContextFactory();
	private readonly Mock<ITillerReceiptCatalog> _catalogMock = new();
	private readonly List<TillerReceiptSnapshot> _snapshots = [];

	private TillerMatchingService CreateService()
	{
		_catalogMock.Setup(c => c.GetReceiptSnapshotsAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((CancellationToken _) => _snapshots.ToList());
		_catalogMock.Setup(c => c.GetReceiptSnapshotAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((Guid id, CancellationToken _) => _snapshots.FirstOrDefault(s => s.ReceiptId == id));
		return new TillerMatchingService(_factory, _catalogMock.Object);
	}

	private Guid AddReceipt(string merchant, DateOnly date, decimal total, bool verified = true)
	{
		Guid id = Guid.NewGuid();
		_snapshots.Add(new TillerReceiptSnapshot(id, merchant, date, total, verified));
		return id;
	}

	private async Task AddTxnAsync(
		string providerId, string date, string description, decimal amount,
		string account = "Chase Checking", string status = "posted")
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
			Account = account,
			Institution = "Chase",
			Status = status,
			Revision = 1,
			CreatedAt = now,
			UpdatedAt = now,
		});
		await context.SaveChangesAsync();
	}

	[Fact]
	public async Task Suggest_ExactAmountMerchantDate_LinksWithExactConfidence()
	{
		// Arrange
		Guid receiptId = AddReceipt("Wholefds Mkt", new DateOnly(2026, 11, 21), 9.99m);
		await AddTxnAsync("t-1", "2026-11-21", "WHOLEFDS MKT #123", -9.99m);

		// Act
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("t-1", CancellationToken.None);

		// Assert
		suggestion.Decision.Should().Be(TillerMatchDecision.Linked);
		suggestion.ReceiptId.Should().Be(receiptId);
		suggestion.Confidence.Should().Be("exact");
	}

	[Fact]
	public async Task Suggest_AmountMismatch_YieldsNone()
	{
		// Arrange
		AddReceipt("Wholefds Mkt", new DateOnly(2026, 11, 21), 9.99m);
		await AddTxnAsync("t-1", "2026-11-21", "WHOLEFDS MKT", -12.00m);

		// Act
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("t-1", CancellationToken.None);

		// Assert
		suggestion.Decision.Should().Be(TillerMatchDecision.None);
		suggestion.ReceiptId.Should().BeNull();
	}

	[Fact]
	public async Task Suggest_SecondReceiptCannotTakeLinkedTransaction()
	{
		// Arrange: link first, then introduce an identical duplicate receipt.
		Guid first = AddReceipt("Wholefds Mkt", new DateOnly(2026, 11, 21), 9.99m);
		await AddTxnAsync("t-1", "2026-11-21", "WHOLEFDS MKT", -9.99m);
		TillerMatchingService service = CreateService();
		(await service.SuggestAsync("t-1", CancellationToken.None)).ReceiptId.Should().Be(first);
		Guid second = AddReceipt("Wholefds Mkt", new DateOnly(2026, 11, 21), 9.99m);

		// Act: ask explicitly about the second receipt.
		TillerMatchSuggestion taken = await service.SuggestForReceiptAsync("t-1", second, CancellationToken.None);

		// Assert
		taken.Decision.Should().Be(TillerMatchDecision.Taken);
	}

	[Fact]
	public async Task Suggest_AmbiguousCandidates_AreListedNeverLinked()
	{
		// Arrange
		await AddTxnAsync("t-1", "2026-11-21", "AMAZON MKTPLACE", -25.00m);
		await AddTxnAsync("t-2", "2026-11-22", "AMAZON MKTPLACE", -25.00m);
		AddReceipt("Amazon Mktplace", new DateOnly(2026, 11, 21), 25.00m);

		// Act
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("t-1", CancellationToken.None);

		// Assert
		suggestion.Decision.Should().Be(TillerMatchDecision.Ambiguous);
		suggestion.ReceiptId.Should().BeNull();
		suggestion.Candidates.Should().HaveCount(2);
	}

	[Fact]
	public async Task ConfirmUnlink_ManualOverridePreservesHistory()
	{
		// Arrange
		await AddTxnAsync("t-1", "2026-11-21", "AMAZON MKTPLACE", -25.00m);
		await AddTxnAsync("t-2", "2026-11-22", "AMAZON MKTPLACE", -25.00m);
		Guid receiptId = AddReceipt("Amazon Mktplace", new DateOnly(2026, 11, 21), 25.00m);
		TillerMatchingService service = CreateService();

		// Act
		TillerMatchSuggestion confirmed = await service.ConfirmAsync("t-2", receiptId, "reviewer:anna", CancellationToken.None);
		TillerMatchSuggestion afterConfirm = await service.SuggestAsync("t-2", CancellationToken.None);
		await service.UnlinkAsync("t-2", "reviewer:anna", CancellationToken.None);
		TillerMatchSuggestion afterUnlink = await service.SuggestAsync("t-2", CancellationToken.None);
		IReadOnlyList<TillerLinkHistoryEntry> history = await service.GetHistoryAsync("t-2", CancellationToken.None);

		// Assert
		confirmed.Decision.Should().Be(TillerMatchDecision.Manual);
		afterConfirm.Decision.Should().Be(TillerMatchDecision.Manual);
		afterConfirm.ReceiptId.Should().Be(receiptId);
		afterUnlink.Decision.Should().Be(TillerMatchDecision.Ambiguous);
		history.Select(h => h.Action).Should().ContainInOrder("confirm", "unlink");
	}

	[Fact]
	public async Task Suggest_UnverifiedReceiptItems_NeverMatch()
	{
		// Arrange
		AddReceipt("Wholefds Mkt", new DateOnly(2026, 11, 21), 9.99m, verified: false);
		await AddTxnAsync("t-1", "2026-11-21", "WHOLEFDS MKT", -9.99m);

		// Act
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("t-1", CancellationToken.None);

		// Assert
		suggestion.Decision.Should().Be(TillerMatchDecision.None);
	}

	[Fact]
	public async Task Confirm_UnverifiedReceipt_ThrowsBeforeAnyLink()
	{
		// Arrange
		Guid receiptId = AddReceipt("Wholefds Mkt", new DateOnly(2026, 11, 21), 9.99m, verified: false);
		await AddTxnAsync("t-1", "2026-11-21", "WHOLEFDS MKT", -9.99m);

		// Act
		Func<Task> act = () => CreateService().ConfirmAsync("t-1", receiptId, null, CancellationToken.None);

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*human-verified*");
	}

	[Fact]
	public async Task Suggest_RefundNeverMatchesPurchaseReceipt()
	{
		// Arrange: money-in (refund) row must not link to a purchase receipt.
		AddReceipt("Wholefds Mkt", new DateOnly(2026, 11, 21), 9.99m);
		await AddTxnAsync("t-1", "2026-11-21", "WHOLEFDS MKT", 9.99m);

		// Act
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("t-1", CancellationToken.None);

		// Assert
		suggestion.Decision.Should().Be(TillerMatchDecision.None);
	}

	[Fact]
	public async Task Suggest_DateOutsideWindow_NeverLinks()
	{
		// Arrange
		AddReceipt("Wholefds Mkt", new DateOnly(2026, 11, 21), 9.99m);
		await AddTxnAsync("t-1", "2026-11-10", "WHOLEFDS MKT", -9.99m);

		// Act
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("t-1", CancellationToken.None);

		// Assert
		suggestion.Decision.Should().Be(TillerMatchDecision.None);
	}

	[Fact]
	public async Task GetPaymentContext_ReturnsLinkedRowFactsForReceipt()
	{
		// Arrange
		Guid receiptId = AddReceipt("Wholefds Mkt", new DateOnly(2026, 11, 21), 9.99m);
		await AddTxnAsync("t-1", "2026-11-21", "WHOLEFDS MKT #123", -9.99m);
		TillerMatchingService service = CreateService();
		await service.SuggestAsync("t-1", CancellationToken.None);

		// Act
		TillerPaymentContext? context = await service.GetPaymentContextAsync(receiptId, CancellationToken.None);

		// Assert
		context.Should().NotBeNull();
		context!.ProviderTransactionId.Should().Be("t-1");
		context.Account.Should().Be("Chase Checking");
	}
}
