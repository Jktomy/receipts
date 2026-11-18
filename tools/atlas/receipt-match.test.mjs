import test from "node:test";
import assert from "node:assert/strict";
import { matchReceiptPurchases } from "./receipt-match.mjs";

function receipt(override = {}) {
  return {
    receiptId: "r1", paperlessDocumentId: "paperless-44",
    merchant: "H-E-B", date: "2026-11-18", amount: "42.75",
    currency: "USD", fireflySourceAccountId: "7",
    ...override
  };
}

function group(id = "31", overrides = {}) {
  return {
    id,
    attributes: {
      transactions: [{
        type: "withdrawal", date: "2026-11-18T08:00:00-06:00",
        amount: "42.75", currency_code: "USD", source_id: "7",
        destination_name: "HEB", description: "Weekly groceries",
        ...overrides
      }]
    }
  };
}

function snapshot(...groups) {
  return { data: groups, links: { next: null }, meta: { pagination: { current_page: 1, total_pages: 1 } } };
}

test("unique exact match is only a proposal and includes no ledger write", () => {
  assert.deepEqual(matchReceiptPurchases([receipt()], snapshot(group())), [
    { receiptId: "r1", status: "PROPOSED_MATCH", fireflyTransactionId: "31" }
  ]);
});

test("transactions from another funding account cannot match", () => {
  assert.equal(matchReceiptPurchases([receipt()], snapshot(group("31", {source_id: "9"})))[0].status, "UNMATCHED");
});

test("wrong currency cannot match even when merchant, amount and date align", () => {
  assert.equal(matchReceiptPurchases([receipt()], snapshot(group("31", {currency_code: "EUR"})))[0].status, "UNMATCHED");
});

test("date drift or merchant difference requires review, not automatic match", () => {
  assert.equal(matchReceiptPurchases([receipt()], snapshot(group("31", {date: "2026-11-19"})))[0].status, "REVIEW");
  assert.equal(matchReceiptPurchases([receipt()], snapshot(group("31", {destination_name: "Walmart"})))[0].status, "REVIEW");
});

test("two same-amount charges remain ambiguous", () => {
  const result = matchReceiptPurchases([receipt()], snapshot(group("31"), group("32")));
  assert.equal(result[0].status, "REVIEW");
  assert.deepEqual(result[0].candidateTransactionIds, ["31", "32"]);
});

test("two receipts cannot both claim a single Firefly transaction", () => {
  const r2 = receipt({receiptId: "r2", paperlessDocumentId: "paperless-45"});
  assert.deepEqual(
    matchReceiptPurchases([receipt(), r2], snapshot(group("31"))).map(r => r.status),
    ["REVIEW", "REVIEW"]
  );
});

test("repeat Paperless source document cannot produce a match", () => {
  const result = matchReceiptPurchases([
    receipt(), receipt({receiptId: "r2"})
  ], snapshot(group("31")));
  assert.deepEqual(result.map(r => r.status), ["REVIEW", "REVIEW"]);
});

test("a split journal is compared by total external charge", () => {
  const g = group("31");
  g.attributes.transactions[0].amount = "40.00";
  g.attributes.transactions.push({
    ...g.attributes.transactions[0], amount: "2.75",
    description: "Tax / second category"
  });
  assert.equal(matchReceiptPurchases([receipt()], snapshot(g))[0].status, "PROPOSED_MATCH");
});

test("inconsistent source account across splits is ignored", () => {
  const g = group("31");
  g.attributes.transactions[0].amount = "40.00";
  g.attributes.transactions.push({
    ...g.attributes.transactions[0], amount: "2.75", source_id: "8"
  });
  assert.equal(matchReceiptPurchases([receipt()], snapshot(g))[0].status, "UNMATCHED");
});

test("refunds, missing currency and fake dates fail closed", () => {
  assert.equal(matchReceiptPurchases([receipt({amount: "-42.75"})], snapshot(group()))[0].status, "REVIEW");
  assert.equal(matchReceiptPurchases([receipt({currency: ""})], snapshot(group()))[0].status, "REVIEW");
  assert.equal(matchReceiptPurchases([receipt({date: "2026-02-30"})], snapshot(group()))[0].status, "REVIEW");
});

test("incomplete Firefly pagination is refused", () => {
  const incomplete = snapshot(group());
  incomplete.links.next = "https://firefly.example/api/v1/transactions?page=2";
  assert.throws(() => matchReceiptPurchases([receipt()], incomplete), /paginated/);
});

test("repeated transaction group IDs are never proposed", () => {
  assert.equal(matchReceiptPurchases([receipt()], snapshot(group("31"), group("31")))[0].status, "REVIEW");
});
