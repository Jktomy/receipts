/**
 * Comparable per-unit pricing for cross-receipt, cross-store comparison.
 * Mirrors Domain.Core.UnitCatalog (C#) — same dimensions, factors and vectors.
 * Keep the two in sync: any factor change here must match the server table.
 * Never converts across dimensions or currencies, and never merges variants:
 * unknown or dimension-mismatched units simply do not compare.
 */

export type UnitDimension = "count" | "mass" | "volume";

const FACTORS: Record<string, { dimension: UnitDimension; toCanonical: number }> = {
  each: { dimension: "count", toCanonical: 1 },
  count: { dimension: "count", toCanonical: 1 },
  g: { dimension: "mass", toCanonical: 1 },
  kg: { dimension: "mass", toCanonical: 1000 },
  oz: { dimension: "mass", toCanonical: 28.349523125 },
  lb: { dimension: "mass", toCanonical: 453.59237 },
  ml: { dimension: "volume", toCanonical: 1 },
  l: { dimension: "volume", toCanonical: 1000 },
  fl_oz: { dimension: "volume", toCanonical: 29.5735295625 },
  cup: { dimension: "volume", toCanonical: 236.5882365 },
  pt: { dimension: "volume", toCanonical: 473.176473 },
  qt: { dimension: "volume", toCanonical: 946.352946 },
  gal: { dimension: "volume", toCanonical: 3785.411784 },
};

export const CANONICAL_UNITS = new Set(Object.keys(FACTORS));

export function normalizeUnit(raw: unknown): string | null | undefined {
  if (raw === undefined || raw === null) return null;
  const key = String(raw).trim().toLowerCase().replace(/[.]+$/, "");
  if (key === "") return null;
  return FACTORS[key] ? key : undefined;
}

export function canonicalUnitFor(dimension: UnitDimension): string {
  if (dimension === "mass") return "g";
  if (dimension === "volume") return "ml";
  return "each";
}

export interface ComparableUnitPrice {
  amountPerCanonicalUnit: number;
  canonicalUnit: string;
  dimension: UnitDimension;
}

/** Price for one canonical unit (each, gram, millilitre), or null when not comparable. */
export function toComparableUnitPrice(
  quantity: number,
  unitPrice: number,
  unitOfMeasure?: string | null,
): ComparableUnitPrice | null {
  if (!Number.isFinite(quantity) || !Number.isFinite(unitPrice) || quantity <= 0 || unitPrice <= 0) return null;
  const normalized = (typeof unitOfMeasure === "string" && unitOfMeasure.trim() !== "" ? unitOfMeasure.trim().toLowerCase() : "each");
  const entry = FACTORS[normalized];
  if (!entry) return null;
  return {
    amountPerCanonicalUnit: unitPrice / entry.toCanonical,
    canonicalUnit: canonicalUnitFor(entry.dimension),
    dimension: entry.dimension,
  };
}

/** Display scaling: grams and millilitres read better per 100 units (grocery convention). */
export interface DisplayUnitPrice {
  amount: number;
  unit: string;
}

export function toDisplayUnitPrice(
  comparable: ComparableUnitPrice,
): DisplayUnitPrice {
  if (comparable.canonicalUnit === "g")
    return { amount: comparable.amountPerCanonicalUnit * 100, unit: "100 g" };
  if (comparable.canonicalUnit === "ml")
    return { amount: comparable.amountPerCanonicalUnit * 100, unit: "100 ml" };
  return { amount: comparable.amountPerCanonicalUnit, unit: "each" };
}

/** True only when both units are known and share one dimension. Variants are never merged on unit alone. */
export function areUnitsComparable(first?: string | null, second?: string | null): boolean {
  const a = typeof first === "string" && first.trim() !== "" ? first.trim().toLowerCase() : "each";
  const b = typeof second === "string" && second.trim() !== "" ? second.trim().toLowerCase() : "each";
  const left = FACTORS[a];
  const right = FACTORS[b];
  return !!left && !!right && left.dimension === right.dimension;
}
