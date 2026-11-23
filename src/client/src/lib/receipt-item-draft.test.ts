import { describe, expect, it } from "vitest";
import { receiptItemDraftSchema } from "./receipt-item-draft";

const valid = {
  receiptItemCode: " X ",
  description: " Milk ",
  quantity: 1,
  unitPrice: 0,
};

describe("receipt item draft", () => {
  it("trims text once and preserves free items and fractional quantities", () => {
    expect(receiptItemDraftSchema.parse({ ...valid, quantity: 0.5 })).toEqual({
      receiptItemCode: "X",
      description: "Milk",
      quantity: 0.5,
      unitPrice: 0,
    });
  });
  it.each(["description"])("rejects whitespace-only %s", (field) => {
    expect(
      receiptItemDraftSchema.safeParse({ ...valid, [field]: " \t " }).success,
    ).toBe(false);
  });
  it.each([0, -1, NaN, Infinity, -Infinity])(
    "rejects quantity %s",
    (quantity) => {
      expect(
        receiptItemDraftSchema.safeParse({ ...valid, quantity }).success,
      ).toBe(false);
    },
  );
  it.each([-1, NaN, Infinity, -Infinity])("rejects price %s", (unitPrice) => {
    expect(
      receiptItemDraftSchema.safeParse({ ...valid, unitPrice }).success,
    ).toBe(false);
  });
  it("defaults optional wizard fields without weakening required text", () => {
    expect(
      receiptItemDraftSchema.parse({
        description: "Milk",
        quantity: 1,
        unitPrice: 0,
      }),
    ).toEqual({
      receiptItemCode: "",
      description: "Milk",
      quantity: 1,
      unitPrice: 0,
    });
  });
});

it("drops retired legacy labels instead of carrying them in the draft", () => {
  const parsed = receiptItemDraftSchema.parse({
    ...valid,
    category: "Food",
    subcategory: "Dairy",
  });
  expect(parsed).not.toHaveProperty("category");
  expect(parsed).not.toHaveProperty("subcategory");
});

describe("receipt item draft units", () => {
  it("accepts a count unit without package content", () => {
    expect(
      receiptItemDraftSchema.parse({ ...valid, unitOfMeasure: "each" }),
    ).toMatchObject({ unitOfMeasure: "each" });
  });

  it("accepts a measured unit without package content (comparability is decided downstream)", () => {
    expect(
      receiptItemDraftSchema.parse({ ...valid, unitOfMeasure: "lb" }),
    ).toMatchObject({ unitOfMeasure: "lb" });
  });

  it("accepts a full package-content combo for measured goods", () => {
    expect(
      receiptItemDraftSchema.parse({
        ...valid,
        unitOfMeasure: "each",
        packageContentQuantity: 64,
        packageContentUnit: "fl_oz",
      }),
    ).toMatchObject({
      unitOfMeasure: "each",
      packageContentQuantity: 64,
      packageContentUnit: "fl_oz",
    });
  });

  it("accepts a count multipack (12 eggs at $4.50)", () => {
    expect(
      receiptItemDraftSchema.parse({
        ...valid,
        unitOfMeasure: "count",
        packageContentQuantity: 12,
        packageContentUnit: "count",
      }),
    ).toMatchObject({
      unitOfMeasure: "count",
      packageContentQuantity: 12,
      packageContentUnit: "count",
    });
  });

  it("normalizes unit case and surrounding whitespace", () => {
    expect(
      receiptItemDraftSchema.parse({ ...valid, unitOfMeasure: " GAL " }),
    ).toMatchObject({ unitOfMeasure: "gal" });
  });

  it("treats blank units as absent", () => {
    const parsed = receiptItemDraftSchema.parse({
      ...valid,
      unitOfMeasure: "",
      packageContentUnit: "  ",
    });
    expect(parsed.unitOfMeasure).toBeUndefined();
    expect(parsed.packageContentUnit).toBeUndefined();
  });

  it("rejects unknown units", () => {
    expect(
      receiptItemDraftSchema.safeParse({ ...valid, unitOfMeasure: "scoop" })
        .success,
    ).toBe(false);
    expect(
      receiptItemDraftSchema.safeParse({
        ...valid,
        unitOfMeasure: "each",
        packageContentQuantity: 64,
        packageContentUnit: "scoop",
      }).success,
    ).toBe(false);
  });

  it("rejects half-modeled package content", () => {
    expect(
      receiptItemDraftSchema.safeParse({
        ...valid,
        unitOfMeasure: "each",
        packageContentQuantity: 64,
      }).success,
    ).toBe(false);
    expect(
      receiptItemDraftSchema.safeParse({
        ...valid,
        unitOfMeasure: "each",
        packageContentUnit: "fl_oz",
      }).success,
    ).toBe(false);
  });

  it("rejects a measured item unit when package content is set", () => {
    expect(
      receiptItemDraftSchema.safeParse({
        ...valid,
        unitOfMeasure: "fl_oz",
        packageContentQuantity: 64,
        packageContentUnit: "fl_oz",
      }).success,
    ).toBe(false);
  });

  it("rejects non-positive package quantities", () => {
    expect(
      receiptItemDraftSchema.safeParse({
        ...valid,
        unitOfMeasure: "each",
        packageContentQuantity: 0,
        packageContentUnit: "fl_oz",
      }).success,
    ).toBe(false);
  });
});
