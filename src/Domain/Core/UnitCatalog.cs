namespace Domain.Core;

/// <summary>
/// Canonical units of measure for purchased quantities, shared by validation,
/// persistence and comparable per-unit pricing. Null or missing means a plain
/// countable item ("each"). Variants, brands and package sizes are never merged
/// here: conversion only applies within one dimension (count, mass, volume).
/// </summary>
public static class UnitCatalog
{
	public const int MaxLength = 16;

	public const string Each = "each";
	public const string Count = "count";

	public static readonly IReadOnlySet<string> CanonicalUnits =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			Each, Count,
			"g", "kg", "oz", "lb",
			"ml", "l", "fl_oz", "cup", "pt", "qt", "gal",
		};

	public enum Dimension
	{
		Count,
		Mass,
		Volume,
	}

	private static readonly IReadOnlyDictionary<string, (Dimension Dimension, decimal ToCanonical)> Factors =
		new Dictionary<string, (Dimension, decimal)>(StringComparer.OrdinalIgnoreCase)
		{
			{ Each, (Dimension.Count, 1m) },
			{ Count, (Dimension.Count, 1m) },
			{ "g", (Dimension.Mass, 1m) },
			{ "kg", (Dimension.Mass, 1000m) },
			{ "oz", (Dimension.Mass, 28.349523125m) },
			{ "lb", (Dimension.Mass, 453.59237m) },
			{ "ml", (Dimension.Volume, 1m) },
			{ "l", (Dimension.Volume, 1000m) },
			{ "fl_oz", (Dimension.Volume, 29.5735295625m) },
			{ "cup", (Dimension.Volume, 236.5882365m) },
			{ "pt", (Dimension.Volume, 473.176473m) },
			{ "qt", (Dimension.Volume, 946.352946m) },
			{ "gal", (Dimension.Volume, 3785.411784m) },
		};

	public static string? Normalize(string? unit)
		=> string.IsNullOrWhiteSpace(unit) ? null : unit.Trim().ToLowerInvariant();

	public static bool IsKnown(string? unit)
		=> !string.IsNullOrWhiteSpace(unit) && CanonicalUnits.Contains(unit.Trim());

	public static bool TryGetDimension(string? unit, out Dimension dimension)
	{
		dimension = Dimension.Count;
		string? normalized = Normalize(unit) ?? Each;
		if (!Factors.TryGetValue(normalized, out var entry))
		{
			return false;
		}

		dimension = entry.Dimension;
		return true;
	}

	public static string CanonicalUnitFor(Dimension dimension) => dimension switch
	{
		Dimension.Mass => "g",
		Dimension.Volume => "ml",
		_ => Each,
	};

	/// <summary>
	/// Comparable price for one canonical unit (each, gram, millilitre).
	/// Returns false for unknown units, non-positive quantities or a dimension
	/// that cannot be compared. Never converts across dimensions or currencies.
	/// </summary>
	public static bool TryGetComparableUnitPrice(
		decimal quantity,
		decimal unitPrice,
		string? unitOfMeasure,
		out decimal amountPerCanonicalUnit,
		out string canonicalUnit)
	{
		=> TryGetComparableUnitPrice(quantity, unitPrice, unitOfMeasure, null, null,
			out amountPerCanonicalUnit, out canonicalUnit);

	/// <summary>
	/// Comparable price for one canonical unit (each, gram, millilitre).
	/// Package-size rule: a mass/volume unit WITHOUT package content is NOT
	/// comparable (the quantity may count packages, not the measured amount).
	/// Supply packageContent (amount per package) to unlock the comparable, or
	/// use a count unit. Never converts across dimensions or currencies.
	/// </summary>
	public static bool TryGetComparableUnitPrice(
		decimal quantity,
		decimal unitPrice,
		string? unitOfMeasure,
		decimal? packageContentQuantity,
		string? packageContentUnit,
		out decimal amountPerCanonicalUnit,
		out string canonicalUnit)
	{
		amountPerCanonicalUnit = 0m;
		canonicalUnit = Each;
		if (quantity <= 0 || unitPrice <= 0)
		{
			return false;
		}

		bool hasContent = packageContentQuantity is not null || packageContentUnit is not null;
		if (hasContent)
		{
			if (packageContentQuantity is null || packageContentQuantity <= 0)
			{
				return false;
			}
			string? contentNormalized = Normalize(packageContentUnit);
			if (contentNormalized is null || !Factors.TryGetValue(contentNormalized, out var contentEntry)
				|| (contentEntry.Dimension != Dimension.Mass && contentEntry.Dimension != Dimension.Volume))
			{
				return false;
			}
			string itemNormalized = Normalize(unitOfMeasure) ?? Each;
			if (!Factors.TryGetValue(itemNormalized, out var itemEntry) || itemEntry.Dimension != Dimension.Count)
			{
				return false;
			}
			amountPerCanonicalUnit = unitPrice / (packageContentQuantity.Value * contentEntry.ToCanonical);
			canonicalUnit = CanonicalUnitFor(contentEntry.Dimension);
			return true;
		}

		string normalized = Normalize(unitOfMeasure) ?? Each;
		if (!Factors.TryGetValue(normalized, out var entry))
		{
			return false;
		}

		if (entry.Dimension != Dimension.Count)
		{
			return false;
		}

		amountPerCanonicalUnit = unitPrice / entry.ToCanonical;
		canonicalUnit = CanonicalUnitFor(entry.Dimension);
		return true;
	}


	public static bool AreComparable(string? first, string? second)
	{
		if (!TryGetDimension(first, out Dimension left) || !TryGetDimension(second, out Dimension right))
		{
			return false;
		}

		return left == right;
	}
}
