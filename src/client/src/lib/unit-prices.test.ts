import { describe, expect, it } from "vitest";
import {
  areUnitsComparable,
  canonicalUnitFor,
  normalizeUnit,
  toComparableUnitPrice,
  toDisplayUnitPrice,
} from "./unit-prices";

describe("unit-prices", () => {
  it("normalizes common spellings to canonical units", () => {
    expect(normalizeUnit("GAL")).toBe("gal");
    expect(normalizeUnit(" L ")).toBe("l");
    expect(normalizeUnit("fl_oz")).toBe("fl_oz");
    expect(normalizeUnit("")).toBeNull();
    expect(normalizeUnit(null)).toBeNull();
    expect(normalizeUnit("litre")).toBeUndefined();
    expect(normalizeUnit("dozen")).toBeUndefined();
  });

  it("compares a gallon against a half-gallon jug per millilitre", () => {
    const gallon = toComparableUnitPrice(1, 3.99, "gal");
    const halfGallonJug = toComparableUnitPrice(64, 2.09 / 64, "fl_oz");
    expect(gallon?.canonicalUnit).toBe("ml");
    expect(halfGallonJug?.canonicalUnit).toBe("ml");
    expect(halfGallonJug!.amountPerCanonicalUnit).toBeGreaterThan(
      gallon!.amountPerCanonicalUnit,
    );
  });

  it("compares weighted produce per gram", () => {
    const perGram = toComparableUnitPrice(1.25, 2.4599, "lb");
    expect(perGram?.canonicalUnit).toBe("g");
    expect(perGram!.amountPerCanonicalUnit).toBeCloseTo(2.4599 / 453.59237, 10);
  });

  it("treats missing units as plain countable items", () => {
    expect(toComparableUnitPrice(2, 3.99, null)?.canonicalUnit).toBe("each");
    expect(toComparableUnitPrice(2, 3.99, undefined)?.amountPerCanonicalUnit).toBe(3.99);
  });

  it("refuses bad quantities, prices and unknown units", () => {
    expect(toComparableUnitPrice(0, 3.99, "gal")).toBeNull();
    expect(toComparableUnitPrice(1, 0, "gal")).toBeNull();
    expect(toComparableUnitPrice(1, 3.99, "litre")).toBeNull();
    expect(toComparableUnitPrice(NaN, 3.99, "gal")).toBeNull();
  });

  it("only matches units within one dimension", () => {
    expect(areUnitsComparable("gal", "l")).toBe(true);
    expect(areUnitsComparable("lb", "oz")).toBe(true);
    expect(areUnitsComparable("each", "count")).toBe(true);
    expect(areUnitsComparable("gal", "lb")).toBe(false);
    expect(areUnitsComparable("ml", "each")).toBe(false);
    expect(areUnitsComparable("gal", "litre")).toBe(false);
  });

  it("maps dimensions to canonical units", () => {
    expect(canonicalUnitFor("mass")).toBe("g");
    expect(canonicalUnitFor("volume")).toBe("ml");
    expect(canonicalUnitFor("count")).toBe("each");
  });
});

describe("toDisplayUnitPrice", () => {
  it("scales grams and millilitres to per-100 display units", () => {
    const perGram = toDisplayUnitPrice({
      amountPerCanonicalUnit: 0.005,
      canonicalUnit: "g",
      dimension: "mass",
    });
    expect(perGram).toEqual({ amount: 0.5, unit: "100 g" });
    const perMl = toDisplayUnitPrice({
      amountPerCanonicalUnit: 0.001,
      canonicalUnit: "ml",
      dimension: "volume",
    });
    expect(perMl).toEqual({ amount: 0.1, unit: "100 ml" });
    const perEach = toDisplayUnitPrice({
      amountPerCanonicalUnit: 4.5,
      canonicalUnit: "each",
      dimension: "count",
    });
    expect(perEach).toEqual({ amount: 4.5, unit: "each" });
  });
});
