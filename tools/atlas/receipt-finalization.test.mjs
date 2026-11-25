// Strict mode trusts the upstream authorized release and checks Paperless revision stability.
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

function harness({ tags = [77], ai = null, flipContent = null, alterDoc = null } = {}) {
  let modelCalls = 0, aiCalls = 0, paperlessReads = 0, docReads = 0, content = ocr;
  const calls = [];
  const oathgateBodies = [];
  const fetcher = async (target, options = {}) => {
    const url = new URL(target);
    calls.push({ host: url.host, pathname: url.pathname, headers: { ...(options.headers ?? {}) } });
    if (url.host === "paperless.test") {
      paperlessReads++;
      if (url.pathname.startsWith("/api/tasks")) throw new Error("Forbidden task dependency");
      if (/\/api\/documents\/\d+\/history\/?$/.test(url.pathname)) {
        return new Response(JSON.stringify(
          [{ action: "update", tagsOp: "add", tagsObjects: [77], ts: "2026-11-20T22:00:01Z", actorId: 2 }]), { status: 200 });
      }
      docReads++;
      if (flipContent) content = flipContent(docReads, content);
      const doc = { id: 9501, tags, modified: "2026-11-20T22:00:00Z", content, versions: [{ added: "2026-11-20T22:00:00Z" }] };
      return new Response(JSON.stringify(alterDoc ? alterDoc(docReads, doc) : doc), { status: 200 });
    }
    if (url.host === "oathgate.test") {
      modelCalls++;
      try { oathgateBodies.push(String(options.body ?? "")); } catch { /* ignore */ }
      return new Response(JSON.stringify({ choices: [{ message: { content: JSON.stringify(good) } }] }), { status: 200 });
    }
    if (url.host === "ai.test") { aiCalls++; throw new Error("Forbidden AI dependency"); }
    throw new Error("UNEXPECTED " + target);
  };
  const config = {
    paperlessUrl: "https://paperless.test", paperlessInstance: "w21-test",
    paperlessToken: "synthetic", requiredTagId: 77, blockedTagIds: [5], authorizedReleaserIds: [2],
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


test("strict mode reads only released Paperless text without AI or task credentials", async () => {
  const h = harness();
  const s = await drive(h);
  assert.equal(s.status, "READY_FOR_REVIEW");
  assert.ok(s.draft);
  assert.equal(h.stats().modelCalls, 1);
  assert.ok(h.stats().paperlessReads >= 3);
  assert.equal(h.stats().aiCalls, 0);
  assert.ok(h.calls.filter(c => c.host === "paperless.test").every(c => c.pathname === "/api/documents/9501/"));
});

test("legacy AI configuration cannot introduce an AI dependency", async () => {
  const h = harness({ ai: "processing" });
  const s = await drive(h);
  assert.equal(s.status, "READY_FOR_REVIEW");
  assert.equal(h.stats().aiCalls, 0);
  assert.equal(h.stats().modelCalls, 1);
});

for (const [name, alterDoc] of [
  ["removed destination tag", (n,d) => n === 2 ? {...d,tags:[]} : d],
  ["protected tag added", (n,d) => n === 2 ? {...d,tags:[77,5]} : d],
  ["missing OCR", (n,d) => ({...d,content:""})],
  ["missing versions", (n,d) => ({...d,versions:[]})],
  ["wrong document ID", (n,d) => n === 2 ? {...d,id:9999} : d],
]) {
  test(name + " fails closed without model calls", async () => {
    const h = harness({alterDoc});
    const s = await drive(h);
    assert.equal(s.status, "REVIEW_REQUIRED");
    assert.equal(h.stats().modelCalls, 0);
  });
}
for (const [name, alterDoc] of [
  ["OCR", (n,d) => n === 2 ? {...d,content:d.content+" FEE 1.00"} : d],
  ["timestamp", (n,d) => n === 2 ? {...d,modified:"2026-11-21T00:00:00Z"} : d],
  ["file version", (n,d) => n === 2 ? {...d,versions:[{added:"2026-11-21T00:00:00Z"}]} : d],
]) {
  test(name + " change during release holds without extraction", async () => {
    const h = harness({alterDoc});
    const s = await drive(h);
    assert.equal(s.status, "RELEASE_UNPROVEN_REVIEW");
    assert.equal(h.stats().modelCalls, 0);
  });
}
test("off mode retains legacy synthetic compatibility", async () => {
  const h = harness();
  delete h.config.finalizationMode;
  const s = await drive(h);
  assert.equal(s.status, "READY_FOR_REVIEW");
  assert.equal(h.stats().aiCalls, 0);
});
test("invalid finalization numerics fail fast", async () => {
  const h = harness();
  for (const extra of [{finalizationStableReads:1},{finalizationMaxAttempts:NaN},{finalizationMode:"bogus"}]) {
    assert.throws(() => createAtlasBridge({...h.config,...extra},{fetcher:h.fetcher,dataDir:mkdtempSync(join(tmpdir(),"w21-bad-"))}),/Unknown finalizationMode|Invalid finalization/);
  }
  const src = {id:9501,modified:"2026-11-20T22:00:00Z",content:ocr,contentHash:createHash("sha256").update(ocr).digest("hex"),versions:[{added:"2026-11-20T22:00:00Z"}]};
  await assert.rejects(verifyFinalOcr(h.fetcher,{...h.config,finalizationMaxAttempts:0},src),/finalizationMaxAttempts/);
});
test("insufficient reads cannot satisfy revision proof", async () => {
  const h = harness();
  h.config.finalizationMaxAttempts=1;
  const s=await drive(h);
  assert.equal(s.status,"REVIEW_REQUIRED");
  assert.equal(h.stats().modelCalls,0);
});
