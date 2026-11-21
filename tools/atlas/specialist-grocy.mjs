// Tracer specialist dispatch: Grocy grocery purchases.
// Native gap (verified against Grocy 4.7.1 upstream behavior): Grocy derives
// price history ONLY from stock bookings, so there is NO stock-neutral
// purchase/price-history write endpoint. This module therefore NEVER posts
// stock by default: dispatchGrocyPurchase returns a HELD proposal (matched
// products or explicit create-new decisions) with zero non-GET calls.
// Actual stock adds require a separate inventory approval plus an explicit
// per-receipt reviewer confirmation (postStockAdd, opt-in only).
// Unknown products are explicit reviewer decisions, never silent creations.

export function matchProduct(products, barcodes, item) {
  const list = Array.isArray(products) ? products : [];
  const codes = Array.isArray(barcodes) ? barcodes : [];
  const sku = (item?.sku ?? "").toString().trim();
  if (sku) {
    const hit = codes.find(b => (b.barcode ?? "").toString() === sku);
    if (hit?.product_id) return { productId: String(hit.product_id), how: "barcode" };
  }
  const name = (item?.description ?? "").toString().trim().toLowerCase();
  if (name) {
    const hit = list.find(p => (p.name ?? "").toString().trim().toLowerCase() === name);
    if (hit?.id) return { productId: String(hit.id), how: "name" };
  }
  return { createNew: true, reason: `No Grocy product matched "${item?.description ?? ""}": reviewer decides` };
}

export function sourceTag(source) {
  return `tracer:paperless:${source.paperlessId}:${String(source.contentHash).slice(0, 8)}`;
}

export function toGrocyProposal(qualified, matches, products, source, barcodes = []) {
  const list = Array.isArray(products) ? products : [];
  const items = Array.isArray(qualified?.items) ? qualified.items : [];
  const lines = items.map((item, index) => {
    const match = matches[index] ?? matchProduct(list, barcodes, item);
    const product = list.find(p => String(p.id) === match.productId);
    const factor = Number(product?.qu_factor_purchase_to_stock);
    const qty = Number(item.quantity);
    const unit = Number(item.unitPrice);
    const lineTotal = Number(item.total);
    const unitPrice = Number.isFinite(unit) ? unit
      : (Number.isFinite(lineTotal) && Number.isFinite(qty) && qty !== 0 ? lineTotal / qty : NaN);
    return {
      description: item.description,
      product_id: match.productId ?? null,
      createNew: match.createNew === true,
      amount: Number.isFinite(qty) && Number.isFinite(factor) && factor > 0 ? Math.round((qty * factor) * 10000) / 10000 : null,
      price: Number.isFinite(unitPrice) ? Math.round(unitPrice * 100) / 100 : null,
      lineTotal: Number.isFinite(lineTotal) ? Math.round(lineTotal * 100) / 100 : null
    };
  });
  return {
    store: qualified.merchant ?? null,
    date: qualified.date ?? null,
    sourceTag: sourceTag(source),
    lines
  };
}

async function readJson(response, service) {
  if (!response.ok) throw new Error(`${service} returned HTTP ${response.status}`);
  return response.json();
}

// Held by default: builds the proposal with read-only data, makes zero
// stock-mutating calls. allowStockMutation requires the separate inventory
// approval AND an explicit reviewer confirmation object.
export async function dispatchGrocyPurchase(fetcher, baseUrl, token, qualified, source, catalog, options = {}) {
  void fetcher; void baseUrl; void token;
  const proposal = toGrocyProposal(qualified, [], catalog.products, source, catalog.barcodes ?? []);
  if (options.allowStockMutation !== true || !options.reviewerConfirmation) {
    return { held: true, proposal, stockCalls: 0 };
  }
  throw new Error("Stock mutation path is staged but not activated: separate inventory approval required");
}

export async function postStockAdd(fetcher, baseUrl, token, productId, amount, price, bestBeforeDate) {
  const url = `${baseUrl}/api/stock/products/${encodeURIComponent(String(productId))}/add`;
  const params = new URLSearchParams({ amount: String(amount), transactiontype: "purchase" });
  if (bestBeforeDate) params.set("bestbeforedate", bestBeforeDate);
  if (price != null) params.set("price", String(price));
  return readJson(await fetcher(`${url}?${params.toString()}`, {
    method: "POST",
    headers: { Authorization: `GrocyApiKey ${token}`, Accept: "application/json" }
  }), "Grocy");
}
