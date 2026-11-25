// Regression: a released stable Paperless receipt must not consult global AI/task queues.
import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createAtlasBridge } from "./receipt-bridge-server.mjs";

const TRACER = 14;
const RETAGGED = 21;
const BLOCKED = [17, 4, 18, 20];
const MOD0 = "2026-11-23T22:00:00Z";
const ADD_AUTH = "2026-11-23T22:30:00Z";
const ocr = ["SYNTH MART 11/23/2026", "MILK 1 3.99", "TOTAL 3.99"].join("\n");
const good = {
  merchant: "SYNTH MART", date: "2026-11-23", currency: "USD",
  merchantEvidence: "SYNTH MART", dateEvidence: "11/23/2026", totalEvidence: "TOTAL 3.99",
  total: "3.99", tax: "0.00", confidence: "high",
  items: [{ description: "Milk", evidence: "MILK 1 3.99", quantity: "1", unitPrice: "3.99", total: "3.99" }],
  adjustments: []
};
const tagAdd = (ts, actorId = 2) => ({ action: "update", tagsOp: "add", tagsObjects: [TRACER], ts, actorId });

function harness() {
  let modelCalls = 0, destCalls = 0, paperlessPosts = 0, taskCalls = 0, aiCalls = 0;
  const fetcher = async (target, options = {}) => {
    const url = new URL(target);
    if (url.host === "paperless.test") {
      if ((options.method ?? "GET").toUpperCase() !== "GET") { paperlessPosts++; }
      if (url.pathname.startsWith("/api/tasks")) {
        taskCalls++; throw new Error("Task endpoint must not be called");
      }
      if (/\/api\/documents\/\d+\/history\/?$/.test(url.pathname)) {
        return new Response(JSON.stringify([{ action: "update", tagsOp: "add", tagsObjects: [TRACER], ts: ADD_AUTH, actorId: 2 }]), { status: 200 });
      }
      return new Response(JSON.stringify(
        { id: 9501, tags: [TRACER, RETAGGED], modified: MOD0, content: ocr, versions: [{ added: MOD0, checksum: null }] }), { status: 200 });
    }
    if (url.host === "oathgate.test") {
      modelCalls++;
      return new Response(JSON.stringify({ choices: [{ message: { content: JSON.stringify(good) } }] }), { status: 200 });
    }
    if (url.host === "ai.test") { aiCalls++; throw new Error("AI endpoint must not be called"); }
    if (url.host === "receipts.test") {
      destCalls++;
      return new Response(JSON.stringify({ receipt: { id: "receipts-9501" } }), { status: 200 });
    }
    throw new Error("UNEXPECTED " + target);
  };
  const config = {
    paperlessUrl: "https://paperless.test", paperlessInstance: "w20-c3",
    paperlessToken: "synthetic", requiredTagId: TRACER, blockedTagIds: BLOCKED,
    openLyricUrl: "https://oathgate.test/v1", openLyricModel: "synth",
    openLyricToken: "synthetic", receiptsUrl: "https://receipts.test",
    receiptsApiKey: "synthetic", webhookSecret: "hook-w20", operatorSecret: "op-w20",
    allowCloudProcessing: true, finalizationMode: "strict",
    finalizationMaxAttempts: 4, finalizationIntervalMs: 1, finalizationStableReads: 2,
    paperlessAiUrl: "https://ai.test", paperlessAiToken: "synthetic-ai",
    authorizedReleaserIds: [2]
  };
  return { fetcher, config, stats: () => ({ modelCalls, destCalls, paperlessPosts, taskCalls, aiCalls }) };
}

test("Released stable document needs neither global queue", async () => {
  const h = harness();
  const dataDir = mkdtempSync(join(tmpdir(), "w20-c3-"));
  const bridge = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  const { server, state } = bridge;
  await new Promise((r) => server.listen(0, "127.0.0.1", r));
  const port = server.address().port;
  try {
    const r = await fetch(`http://127.0.0.1:${port}/hooks/paperless`, {
      method: "POST", headers: { "content-type": "application/json", "x-atlas-hook-key": "hook-w20" },
      body: JSON.stringify({ sourceId: 9501 })
    });
    assert.equal(r.status, 202);
    let s = null;
    for (let i = 0; i < 200; i++) {
      s = state(9501);
      if (s && s.status !== "QUEUED") break;
      await new Promise((x) => setTimeout(x, 50));
    }
    assert.ok(s && s.status !== "QUEUED", "must settle out of QUEUED");
    assert.equal(s.status, "READY_FOR_REVIEW");
    assert.equal(h.stats().modelCalls, 1);
    assert.equal(h.stats().taskCalls, 0);
    assert.equal(h.stats().aiCalls, 0);
    assert.equal(h.stats().destCalls, 0, "zero destination calls");
    assert.equal(h.stats().paperlessPosts, 0, "no Paperless writes");
  } finally {
    server.close();
  }
});
