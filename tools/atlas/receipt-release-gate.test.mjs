// Authenticated destination-tag handoff; synthetic fixtures, no real source writes.
import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createAtlasBridge } from "./receipt-bridge-server.mjs";

const TRACER = 14;
const RETAGGED = 21; // native tag: present on docs, carries NO authority
const REVIEWED = 16;
const SHADOW_PROCESSED = 22;
// Live-grounded protected exclusions per W27 read-only audit (workflow3):
// medical 18, clinical/Endocrinology 17, healthlog 4, plus 20.
const BLOCKED = [17, 4, 18, 20];

const MOD0 = "2026-11-23T22:00:00Z"; // doc modified + V0 added
const ADD_AUTH = "2026-11-23T22:30:00Z"; // distractor 21-add (never authorizes; see destination tests)
const VB = "2026-11-23T23:00:00Z"; // changed-source version B added
const T3 = "2026-11-23T23:30:00Z"; // fresh native re-release add

const ocr = ["SYNTH MART 11/23/2026", "MILK 1 3.99", "BREAD 1 2.75", "TAX 0.61", "TOTAL 7.35"].join("\n");
const ocrV2 = ["SYNTH MART 11/23/2026", "MILK 1 3.99", "BREAD 1 2.75", "FEE 1.00", "TAX 0.61", "TOTAL 7.35"].join("\n");
const ocrThankYou = ocr + "\nTHANK YOU";
const ocrBAG = ocr + "\nBAG 0.00"; // W27 audit boundary content B
const good = {
  merchant: "SYNTH MART", date: "2026-11-23", currency: "USD",
  merchantEvidence: "SYNTH MART", dateEvidence: "11/23/2026", totalEvidence: "TOTAL 7.35",
  total: "7.35", tax: "0.61", confidence: "high",
  items: [
    { description: "Milk", evidence: "MILK 1 3.99", quantity: "1", unitPrice: "3.99", total: "3.99" },
    { description: "Bread", evidence: "BREAD 1 2.75", quantity: "1", unitPrice: "2.75", total: "2.75" }
  ],
  adjustments: []
};
// Native-shaped history entry: 21-add by actorId at ts (mirrors W27 adapter fixtures).
const tagAdd = (ts, actorId = 2) => ({ action: "update", tagsOp: "add", tagsObjects: [TRACER], ts, actorId });
const tagAdd21 = (ts, actorId = 2) => ({ action: "update", tagsOp: "add", tagsObjects: [RETAGGED], ts, actorId });
const V0 = [{ added: MOD0, checksum: null }];
const VBver = [{ added: VB, checksum: null }];

function harness({ tags = [TRACER, RETAGGED], ai = "done", contents = null, auth = [], historyPages = null, docGone = false } = {}) {
  let modelCalls = 0, aiCalls = 0, destCalls = 0, paperlessPosts = 0, historyCalls = 0;
  let docReads = 0;
  let liveTags = [...tags];
  let liveContent = ocr;
  let liveModified = MOD0;
  let liveVersions = V0.map((v) => ({ ...v }));
  let liveHistory = [];
  let gone = docGone;
  const calls = [];
  const fetcher = async (target, options = {}) => {
    const url = new URL(target);
    calls.push({ host: url.host, pathname: url.pathname, method: (options.method ?? "GET").toUpperCase() });
    if (url.host === "paperless.test") {
      if ((options.method ?? "GET").toUpperCase() !== "GET") { paperlessPosts++; }
      if (url.pathname.startsWith("/api/tasks")) {
        return new Response(JSON.stringify({ count: 0, next: null, previous: null, results: [] }), { status: 200 });
      }
      if (/\/api\/documents\/\d+\/history\/?$/.test(url.pathname)) {
        historyCalls++;
        if (historyPages) {
          const page = Number(url.searchParams.get("page") || "1");
          const entries = historyPages.pages[page - 1] ?? [];
          const hasNext = page < historyPages.pages.length;
          return new Response(JSON.stringify({
            count: historyPages.count,
            next: hasNext ? (url.origin + url.pathname + "?page=" + (page + 1)) : null,
            previous: null,
            results: entries
          }), { status: 200 });
        }
        return new Response(JSON.stringify(liveHistory), { status: 200 });
      }
      docReads++;
      if (gone) return new Response(JSON.stringify({ detail: "Not found." }), { status: 404 });
      if (typeof contents === "function") {
        const r = contents(docReads);
        if (r) {
          liveContent = r.content ?? liveContent;
          liveTags = r.tags ?? liveTags;
          if (r.versionAdded) liveVersions = [{ added: r.versionAdded, checksum: null }];
          if (r.modified) liveModified = r.modified;
        }
      }
      return new Response(JSON.stringify(
        { id: 9501, tags: liveTags, modified: liveModified, content: liveContent, versions: liveVersions }), { status: 200 });
    }
    if (url.host === "oathgate.test") {
      modelCalls++;
      return new Response(JSON.stringify({ choices: [{ message: { content: JSON.stringify(good) } }] }), { status: 200 });
    }
    if (url.host === "ai.test") {
      aiCalls++;
      if (url.pathname === "/api/processing/status") {
        return new Response(JSON.stringify(
          { running: true, current: ai === "processing" ? [{ documentId: 9501 }] : [], queuedDocumentIds: [] }), { status: 200 });
      }
      // Installed v2 AI contract (main): OCR record must be done +
      // written-back + never reverted, plus a bound history item.
      if (url.pathname.startsWith("/api/ocr/documents/")) {
        return new Response(JSON.stringify(ai === "done"
          ? { ocr: { status: "done", writtenBack: true, revertedAt: null } }
          : { ocr: { status: "processing", writtenBack: false, revertedAt: null } }), { status: 200 });
      }
      if (url.pathname === "/api/history") {
        return new Response(JSON.stringify(
          { items: ai === "done" ? [{ documentId: 9501, revertedAt: null }] : [] }), { status: 200 });
      }
    }
    if (url.host === "receipts.test") {
      destCalls++;
      return new Response(JSON.stringify({ receipt: { id: "receipts-9501" } }), { status: 200 });
    }
    throw new Error("UNEXPECTED " + target);
  };
  const config = {
    paperlessUrl: "https://paperless.test", paperlessInstance: "w25-test",
    paperlessToken: "synthetic", requiredTagId: TRACER, blockedTagIds: BLOCKED,
    openLyricUrl: "https://oathgate.test/v1", openLyricModel: "synth",
    openLyricToken: "synthetic", receiptsUrl: "https://receipts.test",
    receiptsApiKey: "synthetic", webhookSecret: "hook-w25", operatorSecret: "op-w25",
    allowCloudProcessing: true, finalizationMode: "strict",
    finalizationMaxAttempts: 4, finalizationIntervalMs: 1, finalizationStableReads: 2,
    paperlessAiUrl: "https://ai.test", paperlessAiToken: "synthetic-ai",
    authorizedReleaserIds: auth
  };
  const stats = () => ({ modelCalls, aiCalls, destCalls, paperlessPosts, historyCalls });
  const setLive = (t, c, o = {}) => {
    if (o.gone !== undefined) gone = o.gone;
    if (t) liveTags = [...t];
    if (c !== undefined) liveContent = c;
    if (o.history) liveHistory = o.history;
    if (o.versions) liveVersions = o.versions;
    if (o.modified) liveModified = o.modified;
  };
  return { fetcher, config, calls, stats, setLive };
}

async function drive(h, docId = 9501, body = { sourceId: docId }) {
  const dataDir = mkdtempSync(join(tmpdir(), "w25-rel-"));
  const bridge = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  const { server, state } = bridge;
  await new Promise((r) => server.listen(0, "127.0.0.1", r));
  const port = server.address().port;
  const r = await fetch(`http://127.0.0.1:${port}/hooks/paperless`, {
    method: "POST", headers: { "content-type": "application/json", "x-atlas-hook-key": "hook-w25" },
    body: JSON.stringify(body)
  });
  assert.equal(r.status, 202);
  for (let i = 0; i < 200; i++) {
    const s = state(docId);
    if (s && s.status !== "QUEUED") { server.close(); return { bridge, state: s, dataDir }; }
    await new Promise((x) => setTimeout(x, 50));
  }
  server.close();
  throw new Error("timeout waiting for settle");
}

async function live(h) {
  const dataDir = mkdtempSync(join(tmpdir(), "w25-rel-live-"));
  const bridge = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  const { server, state } = bridge;
  await new Promise((r) => server.listen(0, "127.0.0.1", r));
  const port = server.address().port;
  const call = (method, path, body, kind) => fetch(`http://127.0.0.1:${port}${path}`, {
    method,
    headers: { "content-type": "application/json", [kind === "hook" ? "x-atlas-hook-key" : "x-atlas-operator-key"]: kind === "hook" ? "hook-w25" : "op-w25" },
    body: body ? JSON.stringify(body) : undefined
  });
  const settle = async (body = { sourceId: 9501 }) => {
    const r = await call("POST", "/hooks/paperless", body, "hook");
    assert.equal(r.status, 202);
    for (let i = 0; i < 200; i++) {
      const s = state(9501);
      if (s && s.status !== "QUEUED") return s;
      await new Promise((x) => setTimeout(x, 50));
    }
    throw new Error("timeout waiting for settle");
  };
  return { bridge, state, call, settle, close: () => server.close() };
}

test("missing destination tag id fails fast at startup (no silent misconfiguration)", async () => {
  const h = harness();
  delete h.config.requiredTagId;
  assert.throws(
    () => createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir: mkdtempSync(join(tmpdir(), "w25-bad-")) }),
    /Paperless configuration required/i
  );
});

test("destination tag works without owner-only audit history", async () => {
  const h = harness({ tags: [TRACER], auth: [] });
  const { state } = await drive(h);
  assert.equal(state.status, "READY_FOR_REVIEW");
  assert.equal(h.stats().historyCalls, 0);
  assert.equal(h.stats().modelCalls, 1);
  assert.equal(h.stats().destCalls, 0);
});

test("destination drafts WITHOUT stale release tag: zero model and zero destination calls", async () => {
  const h = harness({ tags: [TRACER], auth: [2] });
  h.setLive([TRACER], undefined, { history: [tagAdd(ADD_AUTH)] });
  const { state } = await drive(h);
  assert.equal(state.status, "READY_FOR_REVIEW");
  assert.equal(h.stats().modelCalls, 1);
  assert.equal(h.stats().destCalls, 0);
});

test("Receipt and legacy tags alone never authorize extraction", async () => {
  const h = harness({ tags: [26, RETAGGED] });
  const { state } = await drive(h);
  assert.equal(state.status, "REVIEW_REQUIRED");
  assert.equal(h.stats().modelCalls, 0);
  assert.equal(h.stats().destCalls, 0);
});

test("Reviewed and Shadow Processed do not substitute for Tracer", async () => {
  const h = harness({ tags: [REVIEWED, SHADOW_PROCESSED] });
  const { state } = await drive(h);
  assert.equal(state.status, "REVIEW_REQUIRED");
  assert.equal(h.stats().modelCalls, 0);
  assert.equal(h.stats().destCalls, 0);
});

test("native authorized destination-add at/after the version floor drafts (positive regime)", async () => {
  const h = harness({ auth: [2] });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const { state } = await drive(h);
  assert.equal(state.status, "READY_FOR_REVIEW");
  assert.ok(state.draft);
  assert.equal(state.releaseBinding?.method, "authenticated-destination-tag");
  assert.equal(h.stats().modelCalls, 1);
  assert.equal(h.stats().destCalls, 0);
  assert.equal(h.stats().paperlessPosts, 0);
  assert.equal(h.stats().historyCalls, 0, "no owner-only native audit history needed");
});

test("forged callback tags are ignored: native source must carry Tracer", async () => {
  const h = harness({ tags: [26] });
  const { state } = await drive(h, 9501, { sourceId: 9501, tags: [TRACER, RETAGGED] });
  assert.equal(state.status, "REVIEW_REQUIRED");
  assert.equal(h.stats().modelCalls, 0);
  assert.equal(h.stats().destCalls, 0);
});

test("release observations are not accepted at the webhook", async () => {
  // Caller-supplied release claims are not authority and are rejected outright
  // so no one mistakes them for an honored channel.
  const h = harness({ auth: [2] });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const { call, close } = await live(h);
  try {
    for (const bad of ["nope", 42, { contentHash: "0".repeat(64) }, { contentHash: "zzz" }]) {
      const r = await call("POST", "/hooks/paperless", { sourceId: 9501, release: bad }, "hook");
      assert.equal(r.status, 400);
    }
    assert.equal(h.stats().modelCalls, 0);
  } finally {
    close();
  }
});

test("protected source with release tag is still denied before any model call", async () => {
  const h = harness({ tags: [TRACER, RETAGGED, 20], auth: [2] });
  h.setLive([TRACER, RETAGGED, 20], undefined, { history: [tagAdd(ADD_AUTH)] });
  const { state } = await drive(h);
  assert.equal(state.status, "REVIEW_REQUIRED");
  assert.match(state.reason ?? "", /excluded sensitive tag/);
  assert.equal(h.stats().modelCalls, 0);
  assert.equal(h.stats().destCalls, 0);
});

test("Missing Paperless OCR with release tag is held, zero model calls", async () => {
  const h = harness({ auth: [2], contents: () => ({content:""}) });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const { state } = await drive(h);
  assert.equal(state.status, "REVIEW_REQUIRED");
  assert.equal(h.stats().modelCalls, 0);
});

test("OCR that keeps changing with release tag is held, zero model calls", async () => {
  const h = harness({ auth: [2], contents: (n) => ({ content: ocr + "\nTICK " + n }) });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const { state } = await drive(h);
  assert.equal(state.status, "RELEASE_UNPROVEN_REVIEW");
  assert.match(state.reason ?? "", /revision changed/i);
  assert.equal(h.stats().modelCalls, 0);
  assert.equal(h.stats().destCalls, 0);
});

test("stale-version release is blocked at import: zero destination calls", async () => {
  const h = harness({ auth: [2] });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const dataDir = mkdtempSync(join(tmpdir(), "w25-stale-"));
  const bridge = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  await bridge.processDocument(9501);
  const ready = bridge.state(9501);
  assert.equal(ready.status, "READY_FOR_REVIEW");
  const rev = ready.draftRevision;
  h.setLive([TRACER, RETAGGED], ocrV2, { versions: VBver, modified: VB });
  const res = await bridge.importReviewed(9501, rev);
  assert.equal(res.status, 409);
  assert.equal(h.stats().destCalls, 0);
  assert.equal(bridge.state(9501).status, "SOURCE_CHANGED_REVIEW");
});

test("retired source blocks import: zero destination calls, draft held for reprocess", async () => {
  const h = harness({ auth: [2] });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const dataDir = mkdtempSync(join(tmpdir(), "w25-retired-"));
  const bridge = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  await bridge.processDocument(9501);
  const ready = bridge.state(9501);
  assert.equal(ready.status, "READY_FOR_REVIEW");
  const rev = ready.draftRevision;
  h.setLive(undefined, undefined, { gone: true });
  const res = await bridge.importReviewed(9501, rev);
  assert.equal(res.status, 409);
  assert.equal(h.stats().destCalls, 0);
  assert.equal(bridge.state(9501).status, "SOURCE_CHANGED_REVIEW");
});

test("destination revoked with UNCHANGED ocr still blocks import: zero destination calls", async () => {
  const h = harness({ auth: [2] });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const dataDir = mkdtempSync(join(tmpdir(), "w25-revoke-"));
  const bridge = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  await bridge.processDocument(9501);
  const ready = bridge.state(9501);
  assert.equal(ready.status, "READY_FOR_REVIEW");
  const rev = ready.draftRevision;
  h.setLive([]);
  const res = await bridge.importReviewed(9501, rev);
  assert.equal(res.status, 409);
  assert.equal(h.stats().destCalls, 0);
  assert.equal(bridge.state(9501).status, "SOURCE_CHANGED_REVIEW");
});

test("retired replay returns the existing receipt with no new destination call", async () => {
  const h = harness({ auth: [2] });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const dataDir = mkdtempSync(join(tmpdir(), "w25-retiredreplay-"));
  const bridge = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  await bridge.processDocument(9501);
  const ready = bridge.state(9501);
  assert.equal(ready.status, "READY_FOR_REVIEW");
  const r1 = await bridge.importReviewed(9501, ready.draftRevision);
  assert.equal(r1.status, 200);
  const callsBefore = h.stats().destCalls;
  assert.equal(callsBefore, 1);
  h.setLive(undefined, undefined, { gone: true });
  const r2 = await bridge.importReviewed(9501, ready.draftRevision);
  assert.equal(r2.status, 200);
  assert.equal(r2.result.receiptId, r1.result.receiptId);
  assert.equal(h.stats().destCalls, 1);
});

test("reviewer correction saves after source retirement: no new admission", async () => {
  const h = harness({ auth: [2] });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const { bridge, state, call, settle, close } = await live(h);
  try {
    const s1 = await settle();
    assert.equal(s1.status, "READY_FOR_REVIEW");
    h.setLive(undefined, undefined, { gone: true });
    const put = await call("PUT", "/drafts/9501", { ...good, merchant: "SYNTH MART Plus", expectedRevision: s1.draftRevision }, "op");
    assert.equal(put.status, 200);
    const saved = state(9501);
    assert.equal(saved.merchant, undefined);
    assert.equal(saved.draft.merchant, "SYNTH MART Plus");
    assert.equal(saved.status, "READY_FOR_REVIEW");
  } finally { close(); }
});

test("duplicate/retry delivery converges: one model call, one draft revision", async () => {
  const h = harness({ auth: [2] });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const dataDir = mkdtempSync(join(tmpdir(), "w25-dup-"));
  const bridge = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  await bridge.processDocument(9501);
  const first = bridge.state(9501);
  await bridge.processDocument(9501);
  const second = bridge.state(9501);
  assert.equal(first.status, "READY_FOR_REVIEW");
  assert.equal(second.draftRevision, first.draftRevision);
  assert.equal(h.stats().modelCalls, 1);
});

test("concurrent duplicate processing makes one model call", async () => {
  const h = harness({ auth: [2] });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const dataDir = mkdtempSync(join(tmpdir(), "w25-conc-"));
  const bridge = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  await Promise.all([bridge.processDocument(9501), bridge.processDocument(9501)]);
  assert.equal(bridge.state(9501).status, "READY_FOR_REVIEW");
  assert.equal(h.stats().modelCalls, 1);
});

test("restart reapply is safe: same revision causes no second model call", async () => {
  const h = harness({ auth: [2] });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const dataDir = mkdtempSync(join(tmpdir(), "w25-restart-"));
  const b1 = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  await b1.processDocument(9501);
  const rev = b1.state(9501).draftRevision;
  const callsAfterFirst = h.stats().modelCalls;
  const b2 = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  await b2.processDocument(9501);
  assert.equal(b2.state(9501).draftRevision, rev);
  assert.equal(h.stats().modelCalls, callsAfterFirst);
});

test("Updated event with new OCR on a clean draft is held pre-model (release does not transfer)", async () => {
  const h = harness({ auth: [2] });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const dataDir = mkdtempSync(join(tmpdir(), "w25-upd-"));
  const bridge = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  await bridge.processDocument(9501);
  const rev1 = bridge.state(9501).draftRevision;
  assert.equal(bridge.state(9501).status, "READY_FOR_REVIEW");
  // Source changes to B while the release tag lingers: the authorizing add
  // predates the new version, so extraction from B is held with zero new calls.
  h.setLive([TRACER, RETAGGED], ocrThankYou, { versions: VBver, modified: VB });
  await bridge.processDocument(9501);
  const s2 = bridge.state(9501);
  assert.equal(s2.status, "RELEASE_STALE_REVIEW");
  assert.equal(s2.draftRevision, rev1);
  assert.equal(h.stats().modelCalls, 1);
  assert.equal(h.stats().destCalls, 0);
});

test("RELEASE_STALE auto-resolves when the source reverts to the released revision", async () => {
  const h = harness({ auth: [2] });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const dataDir = mkdtempSync(join(tmpdir(), "w25-revert-"));
  const bridge = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  await bridge.processDocument(9501);
  const rev1 = bridge.state(9501).draftRevision;
  h.setLive([TRACER, RETAGGED], ocrThankYou, { versions: VBver, modified: VB });
  await bridge.processDocument(9501);
  assert.equal(bridge.state(9501).status, "RELEASE_STALE_REVIEW");
  h.setLive([TRACER, RETAGGED], ocr, { versions: V0.map((v) => ({ ...v })), modified: MOD0 });
  await bridge.processDocument(9501);
  const s3 = bridge.state(9501);
  assert.equal(s3.status, "READY_FOR_REVIEW");
  assert.equal(s3.draftRevision, rev1);
  assert.equal(h.stats().modelCalls, 1);
});

test("changed source requires manual correction, current tag and exact new revision", async () => {
  const h = harness({ tags: [TRACER] });
  const { bridge, state, call, settle, close } = await live(h);
  try {
    const s1 = await settle();
    h.setLive([TRACER], ocrThankYou, { versions: VBver, modified: VB });
    const s2 = await settle();
    assert.equal(s2.status, "RELEASE_STALE_REVIEW");
    assert.equal((await bridge.importReviewed(9501, s1.draftRevision)).status, 409);
    assert.equal(h.stats().destCalls, 0);
    const put = await call("PUT", "/drafts/9501", { ...good, expectedRevision: s1.draftRevision }, "op");
    assert.equal(put.status, 200);
    const saved = state(9501);
    h.setLive([]);
    assert.equal((await bridge.importReviewed(9501, saved.draftRevision)).status, 409);
    assert.equal(h.stats().destCalls, 0);
  } finally { close(); }
});

test("Hold and Clone deny even with authenticated Tracer release", async () => {
  for (const blocked of [28, 29]) {
    const h = harness({ tags: [TRACER, blocked] });
    const { state } = await drive(h);
    assert.equal(state.status, "REVIEW_REQUIRED");
    assert.equal(h.stats().modelCalls, 0);
    assert.equal(h.stats().destCalls, 0);
  }
});

test("file revision change with identical OCR blocks reviewed import", async () => {
  const h = harness({ tags: [TRACER] });
  const bridge = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir: mkdtempSync(join(tmpdir(), "tag-file-change-")) });
  await bridge.processDocument(9501);
  const ready = bridge.state(9501);
  assert.equal(ready.status, "READY_FOR_REVIEW");
  h.setLive([TRACER], ocr, { versions: VBver, modified: VB });
  assert.equal((await bridge.importReviewed(9501, ready.draftRevision)).status, 409);
  assert.equal(h.stats().destCalls, 0);
});

test("release observation must survive mid-wait source changes (proven revision binds)", async () => {
  // The authorized add covers V0; the source settles on B mid-wait with a
  // newer version. The proven revision (B) is what would be extracted — hold.
  const h = harness({ auth: [2], contents: (n) => (n < 2 ? null : { content: ocrBAG, versionAdded: VB, modified: VB }) });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const dataDir = mkdtempSync(join(tmpdir(), "w25-midwait-"));
  const bridge = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  await bridge.processDocument(9501);
  const s = bridge.state(9501);
  assert.equal(s.status, "RELEASE_UNPROVEN_REVIEW");
  assert.equal(h.stats().modelCalls, 0);
  assert.equal(h.stats().destCalls, 0);
});

test("release evidence does not bypass the final-OCR gate", async () => {
  const h = harness({ auth: [2], contents: () => ({content:""}) });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const dataDir = mkdtempSync(join(tmpdir(), "w25-evgate-"));
  const bridge = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  await bridge.processDocument(9501);
  const s = bridge.state(9501);
  assert.equal(s.status, "REVIEW_REQUIRED");
  assert.match(s.reason ?? "", /OCR content is not ready/i);
  assert.equal(h.stats().modelCalls, 0);
});

test("idempotent purchase import posts the destination once and replays the same receipt", async () => {
  const h = harness({ auth: [2] });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  const dataDir = mkdtempSync(join(tmpdir(), "w25-idem-"));
  const bridge = createAtlasBridge(h.config, { fetcher: h.fetcher, dataDir });
  await bridge.processDocument(9501);
  const ready = bridge.state(9501);
  assert.equal(ready.status, "READY_FOR_REVIEW");
  const r1 = await bridge.importReviewed(9501, ready.draftRevision);
  assert.equal(r1.status, 200);
  const r2 = await bridge.importReviewed(9501, ready.draftRevision);
  assert.equal(r2.status, 200);
  assert.equal(r1.result.receiptId, r2.result.receiptId);
  assert.equal(h.stats().destCalls, 1);
});

test("bridge never writes Paperless tags: all Paperless calls are reads", async () => {
  const h = harness({ auth: [2] });
  h.setLive(undefined, undefined, { history: [tagAdd(ADD_AUTH)] });
  await drive(h);
  const writes = h.calls.filter((c) => c.host === "paperless.test" && c.method !== "GET");
  assert.deepEqual(writes, []);
  assert.equal(h.stats().paperlessPosts, 0);
});
