/**
 * Atlas: conservative, OFFLINE candidate matching of itemized receipts to an
 * existing Firefly III transaction export. No API calls or ledger writes.
 * Inputs contain protected financial data; keep them outside the repository.
 *
 * Receipt input: [{receiptId, paperlessDocumentId?, merchant, date, amount,
 *                  currency, fireflySourceAccountId}]
 * Firefly input: GET /api/v1/transactions JSON response, ALL pages collected.
 */
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

function cents(value) {
  const raw = String(value ?? "");
  const match = /^([0-9]+)(?:\.([0-9]{1,2}))?$/.exec(raw);
  if (!match) throw new Error("Expected a positive decimal currency amount with at most two decimal places");
  const result = BigInt(match[1]) * 100n + BigInt((match[2] ?? "").padEnd(2, "0"));
  if (result <= 0n) throw new Error("Zero and negative purchases require separate review");
  return result;
}

function merchantKey(value) {
  return String(value ?? "").normalize("NFKD").toLowerCase().replace(/[^a-z0-9]/g, "");
}

function dayNumber(value) {
  const text = String(value ?? "").slice(0, 10);
  if (!/^\d{4}-\d{2}-\d{2}$/.test(text)) return null;
  const stamp = Date.parse(text + "T00:00:00Z");
  if (!Number.isFinite(stamp) || new Date(stamp).toISOString().slice(0, 10) !== text) return null;
  return stamp / 86400000;
}

function sourceGroups(snapshot) {
  if (!Array.isArray(snapshot?.data)) throw new Error("Expected the Firefly III transactions API JSON with data[]");
  const pg = snapshot.meta?.pagination;
  if (snapshot.links?.next || (pg && Number(pg.current_page) < Number(pg.total_pages))) {
    throw new Error("Firefly export is paginated. Include every page before matching.");
  }
  const groups = [];
  for (const record of snapshot.data) {
    const splits = record?.attributes?.transactions;
    if (!record?.id || !Array.isArray(splits) || splits.length === 0) continue;
    if (!splits.every(s => s?.type === "withdrawal")) continue;
    const accountIds = new Set(splits.map(s => String(s.source_id ?? "")));
    const currencies = new Set(splits.map(s => String(s.currency_code ?? "").toUpperCase()));
    const dates = new Set(splits.map(s => dayNumber(s.date)));
    const merchants = new Set(splits.map(s => merchantKey(s.destination_name || s.description)));
    if (accountIds.size !== 1 || accountIds.has("") ||
        currencies.size !== 1 || currencies.has("") ||
        dates.size !== 1 || dates.has(null) ||
        merchants.size !== 1 || merchants.has("")) continue;
    let total = 0n;
    try { for (const split of splits) total += cents(split.amount); }
    catch { continue; }
    groups.push({
      id: String(record.id), account: [...accountIds][0], currency: [...currencies][0],
      day: [...dates][0], merchant: [...merchants][0], total
    });
  }
  return groups;
}

/**
 * Candidates are suggestions, NEVER authoritative links.
 * "PROPOSED_MATCH" requires an exact account, merchant, amount, currency and
 * calendar date match AND one-to-one uniqueness across both datasets.
 */
export function matchReceiptPurchases(receipts, fireflySnapshot) {
  if (!Array.isArray(receipts)) throw new Error("Expected a receipt array");
  const groups = sourceGroups(fireflySnapshot);
  const seenReceiptIds = new Map(), seenDocumentIds = new Map();
  for (const receipt of receipts) {
    for (const [value, lookup] of [
      [receipt?.receiptId, seenReceiptIds],
      [receipt?.paperlessDocumentId, seenDocumentIds]
    ]) {
      if (value !== undefined && value !== null && String(value) !== "") {
        const id = String(value);
        lookup.set(id, (lookup.get(id) ?? 0) + 1);
      }
    }
  }
  const proposals = receipts.map(receipt => {
    const id = String(receipt?.receiptId ?? "");
    const docId = String(receipt?.paperlessDocumentId ?? "");
    const account = String(receipt?.fireflySourceAccountId ?? "");
    const currency = String(receipt?.currency ?? "").toUpperCase();
    const name = merchantKey(receipt?.merchant);
    const day = dayNumber(receipt?.date);
    let total;
    try { total = cents(receipt?.amount); }
    catch { return { receiptId: id, status: "REVIEW", reason: "Invalid or non-purchase amount" }; }
    if (!id || !account || !name || !day || !/^[A-Z]{3}$/.test(currency)) {
      return { receiptId: id, status: "REVIEW", reason: "Missing exact matching fields" };
    }
    if ((seenReceiptIds.get(id) ?? 0) > 1 || (docId && (seenDocumentIds.get(docId) ?? 0) > 1)) {
      return { receiptId: id, status: "REVIEW", reason: "Repeated receipt or Paperless document" };
    }
    const possible = groups.filter(g =>
      g.account === account && g.currency === currency &&
      g.total === total && Math.abs(g.day - day) <= 2);
    const exact = possible.filter(g => g.day === day && g.merchant === name);
    if (exact.length === 1) {
      return { receiptId: id, status: "PROPOSED_MATCH", fireflyTransactionId: exact[0].id };
    }
    if (exact.length > 1 || possible.length > 0) {
      return {
        receiptId: id, status: "REVIEW", reason: "Ambiguous merchant or date",
        candidateTransactionIds: [...new Set(possible.map(g => g.id))].sort()
      };
    }
    return { receiptId: id, status: "UNMATCHED", reason: "No account/amount/date candidate" };
  });
  const claims = new Map();
  for (const item of proposals) {
    if (item.status === "PROPOSED_MATCH") {
      const id = item.fireflyTransactionId;
      claims.set(id, (claims.get(id) ?? 0) + 1);
    }
  }
  const groupCounts = new Map();
  for (const group of groups) groupCounts.set(group.id, (groupCounts.get(group.id) ?? 0) + 1);
  return proposals.map(item => {
    if (item.status === "PROPOSED_MATCH" &&
       ((claims.get(item.fireflyTransactionId) ?? 0) !== 1 ||
        (groupCounts.get(item.fireflyTransactionId) ?? 0) !== 1)) {
      return { receiptId: item.receiptId, status: "REVIEW", reason: "Non-unique Firefly transaction claim" };
    }
    return item;
  });
}

function main() {
  if (process.argv.length !== 4) {
    console.error("Usage: node tools/atlas/receipt-match.mjs receipts.json firefly-transactions.json");
    process.exitCode = 2;
    return;
  }
  try {
    const receipts = JSON.parse(readFileSync(process.argv[2], "utf8"));
    const firefly = JSON.parse(readFileSync(process.argv[3], "utf8"));
    process.stdout.write(JSON.stringify(matchReceiptPurchases(receipts, firefly), null, 2) + "\n");
  } catch (error) {
    console.error("Matching stopped: " + error.message);
    process.exitCode = 1;
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
