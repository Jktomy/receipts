import { z } from "zod/v4";
import { normalizeUnit } from "./unit-prices";

/**
 * Optional unit of measure. Blank input counts as absent; otherwise the value
 * is trimmed and lowercased and must name a canonical unit (see UnitCatalog).
 * Kept optional so wizard and older callers without unit UI keep working.
 */
const unitField = z
  .string()
  .trim()
  .toLowerCase()
  .optional()
  .transform((v) => (v === undefined || v === "" ? undefined : v));

/** Interaction validation shared by wizard add/edit and persisted-item forms. */
const receiptItemDraftBase = z.object({
  receiptItemCode: z.string().trim().optional().default(""),
  description: z.string().trim().min(1, "Description is required"),
  quantity: z.number().positive("Quantity must be positive"),
  unitPrice: z.number().min(0, "Unit price must be non-negative"),
  unitOfMeasure: unitField,
  packageContentQuantity: z
    .number()
    .positive("Package content quantity must be positive.")
    .optional(),
  packageContentUnit: unitField,
});

export type ReceiptItemDraft = z.output<typeof receiptItemDraftBase>;

/**
 * Cross-field unit rules, mirroring the server validators: package content
 * must be complete (positive quantity plus a known count/mass/volume unit),
 * and the item unit must count packages when content is set. Shared by the
 * draft schema and the extended persisted-item schema (zod v4 forbids
 * .extend() on refined schemas, so both apply this function).
 */
export function checkReceiptItemUnits(
  val: ReceiptItemDraft,
  ctx: z.core.$RefinementCtx,
): void {
    if (
      val.unitOfMeasure !== undefined &&
      normalizeUnit(val.unitOfMeasure) === undefined
    ) {
      ctx.addIssue({
        code: "custom",
        message: "Unit of measure is not a known unit.",
        path: ["unitOfMeasure"],
      });
    }
    const hasContent =
      val.packageContentQuantity !== undefined ||
      val.packageContentUnit !== undefined;
    if (!hasContent) return;
    if (val.packageContentQuantity === undefined) {
      ctx.addIssue({
        code: "custom",
        message: "Package content quantity must be positive.",
        path: ["packageContentQuantity"],
      });
    }
    if (
      val.packageContentUnit === undefined ||
      normalizeUnit(val.packageContentUnit) === undefined
    ) {
      ctx.addIssue({
        code: "custom",
        message:
          "Package content unit is not a known count, mass or volume.",
        path: ["packageContentUnit"],
      });
    }
    if (
      val.unitOfMeasure !== undefined &&
      val.unitOfMeasure !== "each" &&
      val.unitOfMeasure !== "count"
    ) {
      ctx.addIssue({
        code: "custom",
        message: "Unit must count packages when package content is set.",
        path: ["unitOfMeasure"],
      });
    }
  }

export const receiptItemDraftSchema =
  receiptItemDraftBase.superRefine(checkReceiptItemUnits);
