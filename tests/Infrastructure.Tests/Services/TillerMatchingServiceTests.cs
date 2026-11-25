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

	private Guid AddReceipt(string merchant, DateOnly date, decimal total, bool verified = true, decimal tax = 0m, decimal tip = 0m, bool taxUnknown = false)
	{
		Guid id = Guid.NewGuid();
		_snapshots.Add(new TillerReceiptSnapshot(id, merchant, date, total, verified, tax, tip, taxUnknown));
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
	public async Task Suggest_ExactFinalTotalIncludesTaxAndAdjustments()
	{
		Guid id = Guid.NewGuid();
		_snapshots.Add(new TillerReceiptSnapshot(id, "Sample Store", new DateOnly(2026, 1, 1),
			25m, true, TaxTotal: 2m, OtherAdjustmentTotal: -3m));
		await AddTxnAsync("final-total", "2026-01-01", "SAMPLE STORE", -24m);
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("final-total", CancellationToken.None);
		suggestion.Decision.Should().Be(TillerMatchDecision.Linked);
		suggestion.ReceiptId.Should().Be(id);
	}

	[Fact]
	public async Task Suggest_SubtotalAloneDoesNotMatchTaxedReceipt()
	{
		AddReceipt("Sample Store", new DateOnly(2026, 1, 1), 25m, tax: 2m);
		await AddTxnAsync("subtotal", "2026-01-01", "SAMPLE STORE", -25m);
		(await CreateService().SuggestAsync("subtotal", CancellationToken.None)).Decision.Should().Be(TillerMatchDecision.None);
	}

	[Fact]
	public async Task Suggest_TruncatedProcessorMerchantOffersTipWithoutLink()
	{
		Guid id = AddReceipt("La'Moon Coffee and Tea", new DateOnly(2026, 1, 1), 20m, tax: 2m);
		await AddTxnAsync("coffee", "2026-01-03", "TST*LAMOON COFFEE AND T, CITY, TX", -24m);
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("coffee", CancellationToken.None);
		suggestion.Decision.Should().Be(TillerMatchDecision.PossibleTip);
		suggestion.ReceiptId.Should().Be(id);
		suggestion.SuggestedTip.Should().Be(2m);
		await using ApplicationDbContext context = _factory.CreateDbContext();
		(await context.Set<TillerReceiptLinkEntity>().CountAsync()).Should().Be(0);
	}

	[Fact]
	public async Task Suggest_TruncatedMerchantDoesNotAutomaticallyLink()
	{
		AddReceipt("La'Moon Coffee and Tea", new DateOnly(2026, 1, 1), 20m, tax: 2m);
		await AddTxnAsync("coffee", "2026-01-03", "TST*LAMOON COFFEE AND T, CITY, TX", -22m);
		(await CreateService().SuggestAsync("coffee", CancellationToken.None)).Decision.Should().Be(TillerMatchDecision.None);
	}

	[Fact]
	public async Task Suggest_AmountMismatch_YieldsNone()
	{
		// Arrange: a genuinely different amount (delta 5.01 over a 9.99 base
		// exceeds the 25% possible-tip cap) matches nothing.
		AddReceipt("Wholefds Mkt", new DateOnly(2026, 11, 21), 9.99m);
		await AddTxnAsync("t-1", "2026-11-21", "WHOLEFDS MKT", -15.00m);

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

	[Fact]
	public async Task Suggest_UnknownTipPlusSettledCharge_YieldsPossibleTipNeverLinks()
	{
		// Arrange: verified 50.00 receipt with unknown tip; settled 60.00 charge.
		Guid receiptId = AddReceipt("Photo Cafe", new DateOnly(2026, 11, 20), 50.00m);
		await AddTxnAsync("t-tip-1", "2026-11-20", "PHOTO CAFE", -60.00m);

		// Act
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("t-tip-1", CancellationToken.None);

		// Assert: a POSSIBLE 10.00 tip, never an auto-link.
		suggestion.Decision.Should().Be(TillerMatchDecision.PossibleTip);
		suggestion.ReceiptId.Should().Be(receiptId);
		suggestion.SuggestedTip.Should().Be(10.00m);

		await using ApplicationDbContext context = _factory.CreateDbContext();
		(await context.Set<TillerReceiptLinkEntity>().CountAsync()).Should().Be(0);
	}

	[Fact]
	public async Task SuggestForReceipt_UnknownTipPlusSettledCharge_YieldsPossibleTip()
	{
		// Arrange
		Guid receiptId = AddReceipt("Photo Cafe", new DateOnly(2026, 11, 20), 50.00m);
		await AddTxnAsync("t-tip-1", "2026-11-20", "PHOTO CAFE", -60.00m);

		// Act
		TillerMatchSuggestion suggestion = await CreateService().SuggestForReceiptAsync("t-tip-1", receiptId, CancellationToken.None);

		// Assert
		suggestion.Decision.Should().Be(TillerMatchDecision.PossibleTip);
		suggestion.ReceiptId.Should().Be(receiptId);
		suggestion.SuggestedTip.Should().Be(10.00m);
	}

	[Fact]
	public async Task Suggest_TaxedBasePlusSettledCharge_YieldsPossibleTipOnBase()
	{
		// Arrange: items 46.00 + known tax 4.00 = 50.00 base; settled 60.00 charge.
		Guid receiptId = AddReceipt("Photo Cafe", new DateOnly(2026, 11, 20), 46.00m, tax: 4.00m);
		await AddTxnAsync("t-tip-1", "2026-11-20", "PHOTO CAFE", -60.00m);

		// Act
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("t-tip-1", CancellationToken.None);

		// Assert: the tip is computed against items + tax, not items alone.
		suggestion.Decision.Should().Be(TillerMatchDecision.PossibleTip);
		suggestion.ReceiptId.Should().Be(receiptId);
		suggestion.SuggestedTip.Should().Be(10.00m);
	}

	[Fact]
	public async Task Suggest_TipAlreadyRecorded_NeverSuggestsAgain()
	{
		// Arrange: a 5.00 tip is already on the receipt; settled charge covers base + tip.
		AddReceipt("Photo Cafe", new DateOnly(2026, 11, 20), 50.00m, tip: 5.00m);
		await AddTxnAsync("t-tip-1", "2026-11-20", "PHOTO CAFE", -60.00m);

		// Act
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("t-tip-1", CancellationToken.None);

		// Assert
		suggestion.Decision.Should().Be(TillerMatchDecision.None);
	}

	[Fact]
	public async Task Suggest_PendingCharge_NeverSuggestsTip()
	{
		// Arrange: pending charges move; only settled (posted) rows suggest tips.
		AddReceipt("Photo Cafe", new DateOnly(2026, 11, 20), 50.00m);
		await AddTxnAsync("t-tip-1", "2026-11-20", "PHOTO CAFE", -60.00m, status: "pending");

		// Act
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("t-tip-1", CancellationToken.None);

		// Assert
		suggestion.Decision.Should().Be(TillerMatchDecision.None);
	}

	[Fact]
	public async Task Suggest_ExcessiveDelta_NeverSuggestsTip()
	{
		// Arrange: 50.00 over a 50.00 base is a different purchase, not a gratuity.
		AddReceipt("Photo Cafe", new DateOnly(2026, 11, 20), 50.00m);
		await AddTxnAsync("t-tip-1", "2026-11-20", "PHOTO CAFE", -100.00m);

		// Act
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("t-tip-1", CancellationToken.None);

		// Assert
		suggestion.Decision.Should().Be(TillerMatchDecision.None);
	}

	[Fact]
	public async Task Suggest_TaxUnknownBase_NeverSuggestsTip()
	{
		// Arrange: with unknown tax the base is unreliable, so no tip math.
		AddReceipt("Photo Cafe", new DateOnly(2026, 11, 20), 50.00m, taxUnknown: true);
		await AddTxnAsync("t-tip-1", "2026-11-20", "PHOTO CAFE", -60.00m);

		// Act
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("t-tip-1", CancellationToken.None);

		// Assert
		suggestion.Decision.Should().Be(TillerMatchDecision.None);
	}

	[Fact]
	public async Task Suggest_UnverifiedReceipt_NeverSuggestsTip()
	{
		// Arrange
		AddReceipt("Photo Cafe", new DateOnly(2026, 11, 20), 50.00m, verified: false);
		await AddTxnAsync("t-tip-1", "2026-11-20", "PHOTO CAFE", -60.00m);

		// Act
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("t-tip-1", CancellationToken.None);

		// Assert
		suggestion.Decision.Should().Be(TillerMatchDecision.None);
	}

	[Fact]
	public async Task Suggest_ContestedTip_YieldsAmbiguous()
	{
		// Arrange: two identical settled charges both read as a tip on one receipt.
		AddReceipt("Photo Cafe", new DateOnly(2026, 11, 20), 50.00m);
		await AddTxnAsync("t-tip-a", "2026-11-20", "PHOTO CAFE", -60.00m);
		await AddTxnAsync("t-tip-b", "2026-11-20", "PHOTO CAFE", -60.00m);

		// Act
		TillerMatchSuggestion suggestion = await CreateService().SuggestAsync("t-tip-a", CancellationToken.None);

		// Assert: contested, never a lone suggestion.
		suggestion.Decision.Should().Be(TillerMatchDecision.Ambiguous);
		suggestion.SuggestedTip.Should().BeNull();
	}

	[Fact]
	public async Task Confirm_AfterPossibleTip_LinksManualOnly()
	{
		// Arrange: the human confirms the suggested tip link explicitly.
		Guid receiptId = AddReceipt("Photo Cafe", new DateOnly(2026, 11, 20), 50.00m);
		await AddTxnAsync("t-tip-1", "2026-11-20", "PHOTO CAFE", -60.00m);
		TillerMatchingService service = CreateService();

		// Act
		TillerMatchSuggestion confirmation = await service.ConfirmAsync("t-tip-1", receiptId, "tester", CancellationToken.None);

		// Assert
		confirmation.Decision.Should().Be(TillerMatchDecision.Manual);
		confirmation.ReceiptId.Should().Be(receiptId);
		TillerMatchSuggestion recheck = await service.SuggestAsync("t-tip-1", CancellationToken.None);
		recheck.Decision.Should().Be(TillerMatchDecision.Manual);
	}
}
