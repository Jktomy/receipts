using Domain.Core;

namespace Domain.Tests;

public class UnitCatalogTests
{
	[Theory]
	[InlineData("each")]
	[InlineData("COUNT")]
	[InlineData(" G ")]
	[InlineData("kg")]
	[InlineData("OZ")]
	[InlineData("lb")]
	[InlineData("ml")]
	[InlineData("L")]
	[InlineData("fl_oz")]
	[InlineData("CUP")]
	[InlineData("pt")]
	[InlineData("qt")]
	[InlineData("gal")]
	public void IsKnown_RecognizesCanonicalUnitsCaseInsensitively(string unit)
	{
		Assert.True(UnitCatalog.IsKnown(unit));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("litre")]
	[InlineData("pound")]
	[InlineData("dozen")]
	[InlineData("eachs")]
	public void IsKnown_RejectsUnknownUnits(string? unit)
	{
		Assert.False(UnitCatalog.IsKnown(unit));
	}

	[Fact]
	public void Normalize_TrimsAndLowercases()
	{
		Assert.Equal("gal", UnitCatalog.Normalize(" GAL "));
		Assert.Null(UnitCatalog.Normalize("   "));
		Assert.Null(UnitCatalog.Normalize(null));
	}

	[Fact]
	public void ComparableUnitPrice_GallonJug_ComparesPerMillilitre()
	{
		Assert.True(UnitCatalog.TryGetComparableUnitPrice(1m, 3.99m, "each", 1m, "gal", out decimal perMl, out string unit));
		Assert.Equal("ml", unit);
		Assert.Equal(3.99m / 3785.411784m, perMl);
	}

	[Fact]
	public void ComparableUnitPrice_HalfGallonJugCostsMorePerMillilitre_IsDetected()
	{
		Assert.True(UnitCatalog.TryGetComparableUnitPrice(1m, 3.99m, "each", 1m, "gal", out decimal gallonPerMl, out _));
		Assert.True(UnitCatalog.TryGetComparableUnitPrice(1m, 2.09m, "each", 64m, "fl_oz", out decimal halfGallonPerMl, out _));
		Assert.True(halfGallonPerMl > gallonPerMl);
	}

	[Fact]
	public void ComparableUnitPrice_WeighedProduceLot_ComparesPerGram()
	{
		Assert.True(UnitCatalog.TryGetComparableUnitPrice(1m, 3.07m, "each", 1.25m, "lb", out decimal perGram, out string unit));
		Assert.Equal("g", unit);
		Assert.Equal(3.07m / (1.25m * 453.59237m), perGram);
	}

	[Fact]
	public void ComparableUnitPrice_CartonPricesByContent_NotPackageCount()
	{
		Assert.True(UnitCatalog.TryGetComparableUnitPrice(1m, 3.00m, "each", 64m, "fl_oz", out decimal perMl, out string unit));
		Assert.Equal("ml", unit);
		Assert.Equal(3.00m / (64m * 29.5735295625m), perMl);
	}

	[Fact]
	public void ComparableUnitPrice_MassVolumeWithoutContent_IsNotComparable()
	{
		Assert.False(UnitCatalog.TryGetComparableUnitPrice(1m, 3.00m, "fl_oz", out _, out _));
		Assert.False(UnitCatalog.TryGetComparableUnitPrice(1m, 3.99m, "gal", out _, out _));
		Assert.False(UnitCatalog.TryGetComparableUnitPrice(1.25m, 2.4599m, "lb", out _, out _));
	}

	[Fact]
	public void ComparableUnitPrice_HalfModeledOrContradictoryContent_IsNotComparable()
	{
		Assert.False(UnitCatalog.TryGetComparableUnitPrice(1m, 3.00m, "each", 64m, null, out _, out _));
		Assert.False(UnitCatalog.TryGetComparableUnitPrice(1m, 3.00m, "each", null, "fl_oz", out _, out _));
		Assert.False(UnitCatalog.TryGetComparableUnitPrice(1m, 3.00m, "fl_oz", 64m, "fl_oz", out _, out _));
		Assert.False(UnitCatalog.TryGetComparableUnitPrice(1m, 3.00m, "each", 64m, "scoop", out _, out _));
	}

	[Fact]
	public void ComparableUnitPrice_EachItem_ComparesPerEach()
	{
		Assert.True(UnitCatalog.TryGetComparableUnitPrice(12m, 0.50m, "count", out decimal perEach, out string unit));
		Assert.Equal("each", unit);
		Assert.Equal(0.50m, perEach);
	}

	[Fact]
	public void ComparableUnitPrice_NullUnitTreatsAsEach()
	{
		Assert.True(UnitCatalog.TryGetComparableUnitPrice(2m, 3.99m, null, out decimal perEach, out string unit));
		Assert.Equal("each", unit);
		Assert.Equal(3.99m, perEach);
	}

	[Theory]
	[InlineData("0", "3.99", "gal")]
	[InlineData("1", "0", "gal")]
	[InlineData("1", "3.99", "litre")]
	[InlineData("1", "3.99", "dozen")]
	public void ComparableUnitPrice_RejectsBadQuantitiesPricesAndUnits(string quantity, string price, string unit)
	{
		Assert.False(UnitCatalog.TryGetComparableUnitPrice(decimal.Parse(quantity), decimal.Parse(price), unit, out _, out _));
	}

	[Theory]
	[InlineData("gal", "l", true)]
	[InlineData("lb", "oz", true)]
	[InlineData("each", "count", true)]
	[InlineData("gal", "lb", false)]
	[InlineData("ml", "each", false)]
	[InlineData("gal", "litre", false)]
	public void AreComparable_OnlyMatchesSameDimensionKnownUnits(string first, string second, bool expected)
	{
		Assert.Equal(expected, UnitCatalog.AreComparable(first, second));
	}
	[Theory]
	[InlineData(" G ")]
	[InlineData("kg")]
	[InlineData("OZ")]
	[InlineData("lb")]
	[InlineData("ml")]
	[InlineData("L")]
	[InlineData("fl_oz")]
	[InlineData("CUP")]
	[InlineData("pt")]
	[InlineData("qt")]
	[InlineData("gal")]
	public void IsKnown_RecognizesCanonicalUnitsCaseInsensitively(string unit)
	{
		Assert.True(UnitCatalog.IsKnown(unit));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("litre")]
	[InlineData("pound")]
	[InlineData("dozen")]
	[InlineData("eachs")]
	public void IsKnown_RejectsUnknownUnits(string? unit)
	{
		Assert.False(UnitCatalog.IsKnown(unit));
	}

	[Fact]
	public void Normalize_TrimsAndLowercases()
	{
		Assert.Equal("gal", UnitCatalog.Normalize(" GAL "));
		Assert.Null(UnitCatalog.Normalize("   "));
		Assert.Null(UnitCatalog.Normalize(null));
	}
}
