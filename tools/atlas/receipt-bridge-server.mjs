// Atlas Paperless -> Receipts bridge. Opt-in, review-first, no Firefly writers.
// Standalone process belongs to the Receipts stack; no extra OCR engine.
import { createServer } from "node:http";
import { readFileSync, writeFileSync, renameSync, mkdirSync, readdirSync, existsSync } from "node:fs";
import { join } from "node:path";
import { timingSafeEqual } from "node:crypto";
import {
  getPaperlessText, extractUsingOpenLyric, qualifyItemization,
  importQualifiedReceipt, getFireflyWithdrawals, suggestFireflyLink
} from "./receipt-bridge-core.mjs";

function validSecret(actual, expected) {
  if (typeof actual !== "string" || typeof expected !== "string" || !expected) return false;
  const a = Buffer.from(actual), b = Buffer.from(expected);
  return a.length === b.length && timingSafeEqual(a, b);
}

function docIdOf(body) {
  const id = Number(body?.documentId ?? body?.sourceId ?? body?.id);
  if (!Number.isSafeInteger(id) || id < 1 || id > 2147483647) throw new Error("Valid Paperless document ID required");
  return id;
}

function statePath(dir, id) { return join(dir, "paperless-" + id + ".json"); }
function getState(dir, id) {
  const f = statePath(dir, id);
  return existsSync(f) ? JSON.parse(readFileSync(f, "utf8")) : null;
}
function setState(dir, id, state) {
  mkdirSync(dir, { recursive: true, mode: 0o700 });
  const dest = statePath(dir, id), temp = dest + ".tmp-" + process.pid;
  writeFileSync(temp, JSON.stringify(state, null, 2), { mode: 0o600 });
  renameSync(temp, dest);
}
function safeJson(response, status, value) {
  const content = JSON.stringify(value);
  response.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff",
    "Content-Length": Buffer.byteLength(content)
  });
  response.end(content);
}
async function jsonBody(request, max = 8000) {
  let text = "";
  for await (const chunk of request) {
    text += chunk.toString("utf8");
    if (text.length > max) throw new Error("Request body too large");
  }
  return JSON.parse(text || "{}");
}

export function createAtlasBridge(config, { fetcher = globalThis.fetch, dataDir = "./atlas-receipts-data" } = {}) {
  if (!config?.webhookSecret || !config?.operatorSecret) throw new Error("Both access secrets are required");
  if (!config?.paperlessToken || !config?.requiredTagId || !config?.paperlessInstance) throw new Error("Paperless configuration required");
  if (!config?.openLyricModel || !config?.openLyricToken) throw new Error("Open Lyric configuration required");
  if (!config?.receiptsApiKey || !config?.receiptsUrl) throw new Error("Receipts import credentials required");
  mkdirSync(dataDir, { recursive: true, mode: 0o700 });
  const active = new Set();

  async function processDocument(id) {
    if (active.has(id)) return;
    active.add(id);
    try {
      const previous = getState(dataDir, id);
      const source = await getPaperlessText(fetcher, config, id);
      if (previous?.receiptId) {
        const state = source.contentHash === previous.contentHash
          ? { ...previous, status: "IMPORTED" }
          : { ...previous, status: "SOURCE_CHANGED_REVIEW", reason: "Paperless OCR text changed after import" };
        setState(dataDir, id, state);
        return;
      }
      if (previous?.contentHash === source.contentHash && ["READY_FOR_REVIEW", "REVIEW_REQUIRED"].includes(previous.status)) return;
      const parsed = await extractUsingOpenLyric(fetcher, config, source.content);
      const qualified = qualifyItemization(parsed, source.content);
      const state = {
        paperlessId: id, contentHash: source.contentHash, modified: source.modified,
        status: qualified.ok ? "READY_FOR_REVIEW" : "REVIEW_REQUIRED",
        problems: qualified.problems,
        // No original PDF, card number, bank details or full OCR text stored here.
        draft: parsed
      };
      // Auto-import remains disabled by default. It requires an explicit config
      // opt-in AND all independent OCR evidence/decimal checks to pass.
      // Firefly is not modified in either mode.
      if (config.autoImport === true && qualified.ok) {
        const saved = await importQualifiedReceipt(fetcher, config, qualified, id);
        state.status = "IMPORTED";
        state.receiptId = saved.receiptId;
      }
      setState(dataDir, id, state);
    } catch (error) {
      const old = getState(dataDir, id);
      setState(dataDir, id, {
        ...(old ?? { paperlessId: id }),
        status: old?.receiptId ? "SOURCE_CHANGED_REVIEW" : "REVIEW_REQUIRED",
        reason: error instanceof Error ? error.message.slice(0, 160) : "Processing failed"
      });
    } finally {
      active.delete(id);
    }
  }

  async function importReviewed(id) {
    if (active.has(id)) return { status: 409, result: { error: "Processing still active" } };
    const saved = getState(dataDir, id);
    if (!saved?.draft || !saved?.contentHash) return { status: 404, result: { error: "Draft not found" } };
    if (saved.receiptId) return { status: 200, result: { status: "IMPORTED", receiptId: saved.receiptId } };
    const current = await getPaperlessText(fetcher, config, id);
    if (current.contentHash !== saved.contentHash) {
      setState(dataDir, id, { ...saved, status: "SOURCE_CHANGED_REVIEW", reason: "OCR revision changed before import" });
      return { status: 409, result: { error: "Paperless OCR revision changed: reprocess before saving" } };
    }
    const qualified = qualifyItemization(saved.draft, current.content);
    if (!qualified.ok) return { status: 422, result: { error: "Itemization requires correction", problems: qualified.problems } };
    const result = await importQualifiedReceipt(fetcher, config, qualified, id);
    setState(dataDir, id, { ...saved, status: "IMPORTED", receiptId: result.receiptId });
    return { status: 200, result: { status: "IMPORTED", receiptId: result.receiptId } };
  }

  async function matchReviewed(id, sourceAccountId) {
    const saved = getState(dataDir, id);
    if (!saved?.draft || !saved?.contentHash) return { status: 404, result: { error: "Draft not found" } };
    const current = await getPaperlessText(fetcher, config, id);
    if (current.contentHash !== saved.contentHash) return { status: 409, result: { error: "OCR source revision changed" } };
    const qualified = qualifyItemization(saved.draft, current.content);
    if (!qualified.ok) return { status: 422, result: { error: "Receipt is not yet reviewed" } };
    if (!config.fireflyToken || !config.fireflyUrl) return { status: 503, result: { error: "Firefly read-only access not configured" } };
    const day = Date.parse(qualified.date + "T00:00:00Z");
    const start = new Date(day - 2 * 86400000).toISOString().slice(0, 10);
    const end = new Date(Math.min(Date.now(), day + 2 * 86400000)).toISOString().slice(0, 10);
    const snapshot = await getFireflyWithdrawals(fetcher, config, start, end);
    const suggestion = suggestFireflyLink(qualified, id, sourceAccountId, snapshot);
    return { status: 200, result: suggestion };
  }

  const server = createServer(async (request, response) => {
    const url = new URL(request.url ?? "/", "http://localhost");
    const path = url.pathname;
    if (request.method === "GET" && path === "/healthz") {
      return safeJson(response, 200, { status: "ok", fireflyWriteAccess: false, mode: config.autoImport === true ? "QUALIFIED_AUTO_IMPORT" : "REVIEW_ONLY" });
    }
    const isWebhook = request.method === "POST" && path === "/hooks/paperless";
    const authorized = isWebhook
      ? validSecret(request.headers["x-atlas-hook-key"], config.webhookSecret)
      : validSecret(request.headers["x-atlas-operator-key"], config.operatorSecret);
    if (!authorized) return safeJson(response, 401, { error: "Unauthorized" });
    try {
      if (isWebhook) {
        const id = docIdOf(await jsonBody(request));
        if (!config.allowCloudProcessing) return safeJson(response, 503, { error: "Cloud receipt processing not authorized" });
        // Return immediately to Paperless; the receipt job is asynchronous and
        // can be safely requeued by posting the same document ID again.
        if (!active.has(id)) {
          const previous = getState(dataDir, id);
          if (!previous?.receiptId && !["READY_FOR_REVIEW", "REVIEW_REQUIRED"].includes(previous?.status)) {
            setState(dataDir, id, { ...(previous ?? {}), paperlessId: id, status: "QUEUED" });
          }
          void processDocument(id);
        }
        return safeJson(response, 202, { paperlessId: id, status: "ACCEPTED" });
      }
      const match = /^\/drafts\/([1-9][0-9]*)(?:\/(import|match))?$/.exec(path);
      if (!match) return safeJson(response, 404, { error: "Not found" });
      const id = Number(match[1]);
      if (!Number.isSafeInteger(id) || id > 2147483647) return safeJson(response, 400, { error: "Invalid document ID" });
      if (request.method === "GET" && !match[2]) {
        const state = getState(dataDir, id);
        return safeJson(response, state ? 200 : 404, state ?? { error: "Not found" });
      }
      if (request.method === "PUT" && !match[2]) {
        const previous = getState(dataDir, id);
        if (!previous?.contentHash || previous.receiptId) return safeJson(response, 409, { error: "No editable pending draft" });
        const edited = await jsonBody(request, 100000);
        // Still checked against immutable OCR evidence at import time.
        setState(dataDir, id, { ...previous, draft: edited, status: "READY_FOR_REVIEW", problems: [] });
        return safeJson(response, 200, { status: "DRAFT_UPDATED" });
      }
      if (request.method === "POST" && match[2] === "import") {
        const result = await importReviewed(id);
        return safeJson(response, result.status, result.result);
      }
      if (request.method === "POST" && match[2] === "match") {
        const body = await jsonBody(request);
        const result = await matchReviewed(id, String(body.fireflySourceAccountId ?? ""));
        return safeJson(response, result.status, result.result);
      }
      return safeJson(response, 405, { error: "Method not allowed" });
    } catch (error) {
      // No tokens, OCR, model output, or payment data in errors/logs.
      const reason = error instanceof Error ? error.message : "Unexpected error";
      return safeJson(response, 422, { error: reason.slice(0, 160) });
    }
  });

  // Pending work is intentionally not auto-replayed after an unclean restart.
  // Paperless's native manual workflow and re-tagging can retrigger an ID safely.
  // Avoid claiming unattended durable recovery without a proper queue.
  return { server, processDocument, importReviewed, matchReviewed, state: id => getState(dataDir, id) };
}

function secret(name) {
  const file = process.env[name + "_FILE"];
  if (!file) throw new Error(name + "_FILE is required (use private file secrets)");
  return readFileSync(file, "utf8").trim();
}

if (process.argv[1] && /(?:^|[\\/])receipt-bridge-server\.mjs$/.test(process.argv[1])) {
  const config = {
    paperlessUrl: process.env.ATLAS_PAPERLESS_URL,
    paperlessInstance: process.env.ATLAS_PAPERLESS_INSTANCE,
    requiredTagId: Number(process.env.ATLAS_PAPERLESS_RECEIPT_TAG_ID),
    blockedTagIds: String(process.env.ATLAS_PAPERLESS_BLOCKED_TAG_IDS ?? "").split(",").filter(Boolean).map(Number),
    paperlessToken: secret("ATLAS_PAPERLESS_TOKEN"),
    openLyricUrl: process.env.ATLAS_OPEN_LYRIC_URL,
    openLyricModel: process.env.ATLAS_OPEN_LYRIC_MODEL || "Open_Lyric",
    openLyricToken: secret("ATLAS_OPEN_LYRIC_TOKEN"),
    receiptsUrl: process.env.ATLAS_RECEIPTS_URL,
    receiptsApiKey: secret("ATLAS_RECEIPTS_API_KEY"),
    fireflyUrl: process.env.ATLAS_FIREFLY_URL,
    fireflyToken: process.env.ATLAS_FIREFLY_TOKEN_FILE ? secret("ATLAS_FIREFLY_TOKEN") : undefined,
    webhookSecret: secret("ATLAS_WEBHOOK_SECRET"),
    operatorSecret: secret("ATLAS_OPERATOR_SECRET"),
    allowCloudProcessing: process.env.ATLAS_ALLOW_CLOUD_RECEIPT_PROCESSING === "1",
    autoImport: process.env.ATLAS_AUTO_IMPORT_ENABLED === "1"
  };
  const bind = process.env.ATLAS_BRIDGE_BIND || "127.0.0.1";
  const port = Number(process.env.ATLAS_BRIDGE_PORT || 8675);
  if (!Number.isSafeInteger(port) || port < 1 || port > 65535) throw new Error("Invalid port");
  const { server } = createAtlasBridge(config, { dataDir: process.env.ATLAS_BRIDGE_DATA_DIR || "./atlas-receipts-data" });
  server.listen(port, bind, () => console.log("Atlas receipt bridge listening (review-only; no Firefly write access)"));
}
