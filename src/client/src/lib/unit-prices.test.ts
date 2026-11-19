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

  it("compares a gallon jug against a half-gallon jug per millilitre", () => {
    const gallon = toComparableUnitPrice(1, 3.99, "each", 1, "gal");
    const halfGallonJug = toComparableUnitPrice(1, 2.09, "each", 64, "fl_oz");
    expect(gallon?.canonicalUnit).toBe("ml");
    expect(halfGallonJug?.canonicalUnit).toBe("ml");
    expect(halfGallonJug!.amountPerCanonicalUnit).toBeGreaterThan(
      gallon!.amountPerCanonicalUnit,
    );
  });

  it("prices a carton by its package content, not the package count", () => {
    // Hoid repro: $3.00 for 1 carton holding 64 fl oz. Naive per-fl-oz math
    // reads $10.14 per 100 mL; content-aware math reads about $0.16.
    const carton = toComparableUnitPrice(1, 3.0, "each", 64, "fl_oz");
    expect(carton?.canonicalUnit).toBe("ml");
    const display = toDisplayUnitPrice(carton!);
    expect(display.unit).toBe("100 ml");
    expect(display.amount).toBeCloseTo(0.16, 2);
  });

  it("refuses mass/volume without package content as ambiguous", () => {
    // A bare fl_oz quantity may count packages, not ounces: never label it.
    expect(toComparableUnitPrice(1, 3.0, "fl_oz")).toBeNull();
    expect(toComparableUnitPrice(1, 3.99, "gal")).toBeNull();
    expect(toComparableUnitPrice(1.25, 2.4599, "lb")).toBeNull();
  });

  it("compares weighed produce once its lot content is modeled", () => {
    const lot = toComparableUnitPrice(1, 3.07, "each", 1.25, "lb");
    expect(lot?.canonicalUnit).toBe("g");
    expect(lot!.amountPerCanonicalUnit).toBeCloseTo(3.07 / (1.25 * 453.59237), 10);
  });

  it("rejects half-modeled or contradictory package content", () => {
    expect(toComparableUnitPrice(1, 3.0, "each", 64, null)).toBeNull();
    expect(toComparableUnitPrice(1, 3.0, "each", null, "fl_oz")).toBeNull();
    expect(toComparableUnitPrice(1, 3.0, "fl_oz", 64, "fl_oz")).toBeNull();
    expect(toComparableUnitPrice(1, 3.0, "each", 64, "scoop")).toBeNull();
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
