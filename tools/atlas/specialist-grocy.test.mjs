import assert from "node:assert/strict";
import test from "node:test";
import { matchProduct, toGrocyProposal, dispatchGrocyPurchase } from "./specialist-grocy.mjs";

const products = [
  { id: "7", name: "Whole Milk", qu_factor_purchase_to_stock: 1, quantity_unit_id_stock: "19" },
  { id: "9", name: "Eggs Large 12ct", qu_factor_purchase_to_stock: 1, quantity_unit_id_stock: "19" }
];
const barcodes = [{ barcode: "041900001129", product_id: "7" }];

test("exact barcode match resolves the product", () => {
  const m = matchProduct(products, barcodes, { description: "Milk", sku: "041900001129" });
  assert.deepEqual(m, { productId: "7", how: "barcode" });
});

test("name match resolves case-insensitively", () => {
  const m = matchProduct(products, barcodes, { description: "eggs large 12ct" });
  assert.deepEqual(m, { productId: "9", how: "name" });
});

test("unknown product stays an explicit reviewer decision, never a silent creation", () => {
  const m = matchProduct(products, barcodes, { description: "Dragonfruit" });
  assert.equal(m.createNew, true);
  assert.equal(m.productId ?? null, null);
});

test("proposal converts quantities and carries the source tag", () => {
  const qualified = {
    merchant: "H-E-B", date: "2026-11-20",
    items: [{ description: "Milk", quantity: "2", unitPrice: "3.99", total: "7.98" }],
    total: "7.98"
  };
  const p = toGrocyProposal(qualified, [{ productId: "7", how: "barcode" }], products, { paperlessId: 9401, contentHash: "abc123" });
  assert.equal(p.store, "H-E-B");
  assert.equal(p.lines[0].product_id, "7");
  assert.equal(p.lines[0].amount, 2);
  assert.equal(p.lines[0].price, 3.99);
  assert.match(p.sourceTag, /tracer:paperless:9401:abc123/);
});

test("held dispatch makes zero stock calls by default", async () => {
  const calls = [];
  const fetcher = async (url, options = {}) => {
    calls.push({ url, method: options.method ?? "GET" });
    return new Response(JSON.stringify([]), { status: 200, headers: { "content-type": "application/json" } });
  };
  const qualified = {
    merchant: "H-E-B", date: "2026-11-20",
    items: [{ description: "Milk", quantity: "2", unitPrice: "3.99", total: "7.98" }],
    total: "7.98"
  };
  const out = await dispatchGrocyPurchase(fetcher, "https://grocy.test", "token", qualified, { paperlessId: 9401, contentHash: "abc123" }, { products, barcodes });
  assert.equal(out.held, true);
  assert.equal(calls.filter(c => c.method !== "GET").length, 0);
});
