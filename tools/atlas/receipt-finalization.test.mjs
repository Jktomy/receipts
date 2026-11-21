// TDD RED: finalization gate — an Updated event is a SIGNAL, not proof.
// Strict mode must fail closed until doc-ID-bound completion evidence exists.
import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createAtlasBridge } from "./receipt-bridge-server.mjs";
import { verifyFinalOcr } from "./receipt-bridge-core.mjs";

const ocr = ["SYNTH MART 11/20/2026", "MILK 1 3.99", "BREAD 1 2.75", "TAX 0.61", "TOTAL 7.35"].join("\n");
const good = {
  merchant: "SYNTH MART", date: "2026-11-20", currency: "USD",
  merchantEvidence: "SYNTH MART", dateEvidence: "11/20/2026", totalEvidence: "TOTAL 7.35",
  total: "7.35", tax: "0.61", confidence: "high",
  items: [
    { description: "Milk", evidence: "MILK 1 3.99", quantity: "1", unitPrice: "3.99", total: "3.99" },
    { description: "Bread", evidence: "BREAD 1 2.75", quantity: "1", unitPrice: "2.75", total: "2.75" }
  ],
  adjustments: []
};

function harness({ tags = [77], ai = null, flipContent = null } = {}) {
  let modelCalls = 0, aiCalls = 0, paperlessReads = 0, docReads = 0, content = ocr;
  const calls = [];
  const oathgateBodies = [];
  const fetcher = async (target, options = {}) => {
    const url = new URL(target);
    calls.push({ host: url.host, pathname: url.pathname, headers: { ...(options.headers ?? {}) } });
    if (url.host === "paperless.test") {
      paperlessReads++;
      if (url.pathname.startsWith("/api/tasks")) {
        return new Response(JSON.stringify({ count: 0, next: null, previous: null, results: [] }), { status: 200 });
      }
      docReads++;
      if (flipContent) content = flipContent(docReads, content);
      return new Response(JSON.stringify(
        { id: 9501, tags, modified: "2026-11-20T22:00:00Z", content }), { status: 200 });
    }
    if (url.host === "oathgate.test") {
      modelCalls++;
      try { oathgateBodies.push(String(options.body ?? "")); } catch { /* ignore */ }
      return new Response(JSON.stringify({ choices: [{ message: { content: JSON.stringify(good) } }] }), { status: 200 });
    }
    if (url.host === "ai.test") {
      aiCalls++;
      const state = typeof ai === "function" ? ai(aiCalls) : ai;
      if (url.pathname === "/api/processing/status") {
        return new Response(JSON.stringify(
          { running: true, current: state === "processing" ? [{ documentId: 9501 }] : [], queuedDocumentIds: [] }), { status: 200 });
      }
      if (url.pathname === "/api/history") {
        if (state === "wrongdoc") {
          return new Response(JSON.stringify({ entries: [{ documentId: 9999, status: "done" }] }), { status: 200 });
        }
        return new Response(JSON.stringify(
          { entries: state === "done" ? [{ documentId: 9501, status: "done" }] : [] }), { status: 200 });
      }
    }
    throw new Error("UNEXPECTED " + target);
  };
  const config = {
    paperlessUrl: "https://paperless.test", paperlessInstance: "w21-test",
    paperlessToken: "synthetic", requiredTagId: 77, blockedTagIds: [5],
    openLyricUrl: "https://oathgate.test/v1", openLyricModel: "synth",
    openLyricToken: "synthetic", receiptsUrl: "https://receipts.test",
    receiptsApiKey: "synthetic", webhookSecret: "hook-w21", operatorSecret: "op-w21",
    allowCloudProcessing: true, finalizationMode: "strict",
    finalizationMaxAttempts: 4, finalizationIntervalMs: 1, finalizationStableReads: 2,
    ...(ai === null ? {} : { paperlessAiUrl: "https://ai.test", paperlessAiToken: "synthetic-ai" })
  };
  return { fetcher, config, calls, oathgateBodies, stats: () => ({ modelCalls, aiCalls, paperlessReads }) };
}

async function drive(h, docId = 9501) {
  const dataDir = mkdtempSync(join(tmpdir(), "w21-final-"));
  const { server, state } = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  await new Promise((r) => server.listen(0, "127.0.0.1", r));
  const port = server.address().port;
  const req = (p, m, b, k, v) => fetch(`http://127.0.0.1:${port}${p}`,
    { method: m, headers: { "content-type": "application/json", [k]: v }, body: b ? JSON.stringify(b) : undefined });
  const r = await req("/hooks/paperless", "POST", { sourceId: docId }, "x-atlas-hook-key", "hook-w21");
  assert.equal(r.status, 202);
  for (let i = 0; i < 200; i++) {
    const s = state(docId);
    if (s && s.status !== "QUEUED") { server.close(); return s; }
    await new Promise((x) => setTimeout(x, 50));
  }
  server.close();
  throw new Error("timeout waiting for settle");
}

test("strict mode without AI endpoint fails closed with zero model calls", async () => {
  const h = harness({ ai: null });
  const s = await drive(h);
  assert.equal(s.status, "REVIEW_REQUIRED");
  assert.match(s.reason, /final OCR/i);
  assert.equal(s.draft, undefined);
  assert.equal(h.stats().modelCalls, 0);
});

test("strict mode waits while AI reports processing, then proceeds when done", async () => {
  const h = harness({ ai: (n) => (n <= 2 ? "processing" : "done") });
  const s = await drive(h);
  assert.equal(s.status, "READY_FOR_REVIEW");
  assert.ok(s.draft);
  assert.equal(h.stats().modelCalls, 1);
  assert.ok(h.stats().paperlessReads > 1, "expected bounded re-fetch retries");
  const aiCall = h.calls.find((c) => c.host === "ai.test");
  assert.ok(aiCall && aiCall.headers && aiCall.headers["x-api-key"] === "synthetic-ai", "Paperless-AI native auth must be x-api-key");
});

test("strict mode fails closed when OCR keeps changing", async () => {
  const h = harness({ ai: "done", flipContent: (n, c) => c + "." });
  const s = await drive(h);
  assert.equal(s.status, "REVIEW_REQUIRED");
  assert.match(s.reason, /final OCR|stable/i);
  assert.equal(h.stats().modelCalls, 0);
});

test("off mode preserves existing behavior (regression guard)", async () => {
  const h = harness({ ai: "done" });
  delete h.config.finalizationMode;
  const s = await drive(h);
  assert.equal(s.status, "READY_FOR_REVIEW");
  assert.equal(h.stats().modelCalls, 1);
  assert.equal(h.stats().aiCalls, 0);
});

test("protected tags are denied before any finalization check", async () => {
  const h = harness({ tags: [77, 5], ai: "done" });
  const s = await drive(h);
  assert.equal(s.status, "REVIEW_REQUIRED");
  assert.match(s.reason, /excluded sensitive tag/);
  assert.equal(h.stats().modelCalls, 0);
  assert.equal(h.stats().aiCalls, 0);
});

test("strict mode drafts from the proven revision, not the superseded original", async () => {
  const h = harness({ ai: "done", flipContent: (n, c) => (n === 2 ? c + "\nFEE 1.00" : c) });
  const s = await drive(h);
  assert.equal(s.status, "READY_FOR_REVIEW");
  assert.equal(h.stats().modelCalls, 1);
  const provenContent = ocr + "\nFEE 1.00";
  const provenHash = createHash("sha256").update(provenContent, "utf8").digest("hex");
  assert.equal(s.contentHash, provenHash);
  assert.ok(h.oathgateBodies.some((b) => b.includes("FEE 1.00")), "model must see the proven revision");
});

test("Paperless-AI history bound to another document fails closed", async () => {
  const h = harness({ ai: "wrongdoc" });
  const s = await drive(h);
  assert.equal(s.status, "REVIEW_REQUIRED");
  assert.match(s.reason, /final OCR/i);
  assert.equal(s.draft, undefined);
  assert.equal(h.stats().modelCalls, 0);
});

test("invalid finalization numerics fail fast at startup", async () => {
  const bad = [
    { finalizationMode: "strict", finalizationStableReads: 1 },
    { finalizationMode: "strict", finalizationMaxAttempts: NaN },
    { finalizationMode: "bogus" }
  ];
  for (const extra of bad) {
    const h = harness({ ai: "done" });
    assert.throws(
      () => createAtlasBridge({ ...h.config, ...extra }, { fetcher: h.fetcher, dataDir: mkdtempSync(join(tmpdir(), "w21-bad-")) }),
      /Unknown finalizationMode|Invalid finalization/
    );
  }
});

test("verifyFinalOcr requires AI evidence even when text is stable", async () => {
  const h = harness({ ai: null });
  const src = { id: 9501, modified: "2026-11-20T22:00:00Z", content: ocr, contentHash: "x" };
  const r = await verifyFinalOcr(h.fetcher, h.config, src, { sleep: async () => {} });
  assert.equal(r.ok, false);
  assert.match(r.reason, /final OCR/i);
});

test("string document ID still sees AI processing (type-safe doc match)", async () => {
  const h = harness({ ai: "processing" });
  const src = { id: "9501", modified: "2026-11-20T22:00:00Z", content: ocr, contentHash: "x" };
  const r = await verifyFinalOcr(h.fetcher, h.config, src, { sleep: async () => {} });
  assert.equal(r.ok, false);
  assert.match(r.reason, /processing/);
  assert.equal(h.stats().modelCalls, 0);
});

test("verifyFinalOcr rejects unusable numerics fail-fast instead of crashing", async () => {
  const h = harness({ ai: "done" });
  const src = { id: 9501, modified: "2026-11-20T22:00:00Z", content: ocr, contentHash: "x" };
  await assert.rejects(verifyFinalOcr(h.fetcher, { ...h.config, finalizationMaxAttempts: 0 }, src, { sleep: async () => {} }), /finalizationMaxAttempts/);
  await assert.rejects(verifyFinalOcr(h.fetcher, { ...h.config, finalizationStableReads: 1 }, src, { sleep: async () => {} }), /finalizationStableReads/);
});
