// Atlas Paperless -> Receipts bridge. Opt-in, review-first, no Firefly writers.
// Standalone process belongs to the Receipts stack; no extra OCR engine.
import { createServer } from "node:http";
import { readFileSync, writeFileSync, renameSync, mkdirSync, readdirSync, existsSync } from "node:fs";
import { join } from "node:path";
import { timingSafeEqual } from "node:crypto";
import {
  getPaperlessText, extractUsingOpenLyric, qualifyItemization, sanitizePurchaseDraft,
  importQualifiedReceipt, getFireflyWithdrawals, suggestFireflyLink, authorizeImport,
  verifyFinalOcr, classifyRoutes, probeSourceStatus
} from "./receipt-bridge-core.mjs";

function validSecret(actual, expected) {
  if (typeof actual !== "string" || typeof expected !== "string" || !expected) return false;
  const a = Buffer.from(actual), b = Buffer.from(expected);
  return a.length === b.length && timingSafeEqual(a, b);
}

function bodyError(message, statusCode) {
  const error = new Error(message);
  error.statusCode = statusCode;
  return error;
}

// ReHarvest alone authorizes assignment of the destination tag. Trust the
// existing authenticated Paperless workflow and re-read native source state.
// Caller-supplied release claims or tags are never accepted as evidence.
function rejectReleaseClaim(body) {
  if (body?.release !== undefined && body?.release !== null) {
    throw bodyError("Release observations are not accepted here; use the authenticated Paperless destination-tag workflow", 400);
  }
}

function docIdOf(body) {
  const id = Number(body?.documentId ?? body?.sourceId ?? body?.id);
  if (!Number.isSafeInteger(id) || id < 1 || id > 2147483647) throw bodyError("Valid Paperless document ID required", 400);
  return id;
}

// A tag is document-scoped; preserve the observed OCR and native file revision
// across finalization, extraction and import. Missing version evidence holds.
function sameSourceRevision(a, b) {
  return Boolean(a?.contentHash && a.contentHash === b?.contentHash &&
    Array.isArray(a.versions) && a.versions.length &&
    Array.isArray(b.versions) && JSON.stringify(a.versions) === JSON.stringify(b.versions));
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
function findOtherClaimant(dir, fireflyTransactionId, exceptId) {
  // Global one-to-one guard: a single Firefly payment may back at most one
  // receipt. Reads every draft synchronously (no awaits) so the caller's
  // check-and-set stays atomic on Node's single thread.
  let names = [];
  try { names = readdirSync(dir); } catch { return null; }
  for (const name of names) {
    const m = /^paperless-([1-9][0-9]*)\.json$/.exec(name);
    if (!m) continue;
    const otherId = Number(m[1]);
    if (otherId === exceptId) continue;
    let state = null;
    try { state = JSON.parse(readFileSync(join(dir, name), "utf8")); } catch { continue; }
    if (state && state.confirmedFireflyTransactionId === fireflyTransactionId) return otherId;
  }
  return null;
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
async function markAvailable() { return { status: "AVAILABLE", checkedAt: new Date().toISOString() }; }
async function classifySourceFailure(fetcher, config, id, error) {
  const checkedAt = new Date().toISOString();
  const msg = error instanceof Error ? error.message : String(error);
  const m = /HTTP (\d{3})/.exec(msg);
  const code = m ? Number(m[1]) : null;
  if (code === 401 || code === 403) return { status: "DENIED", checkedAt };
  if (code === 404 && config.paperlessTrashPath) {
    const probe = await probeSourceStatus(fetcher, config, id);
    return { status: probe.status, checkedAt };
  }
  return { status: "UNKNOWN", checkedAt };
}
async function jsonBody(request, max = 8000) {
  let text = "";
  for await (const chunk of request) {
    text += chunk.toString("utf8");
    if (text.length > max) throw bodyError("Request body too large", 413);
  }
  try {
    return JSON.parse(text || "{}");
  } catch {
    throw bodyError("Malformed request body: invalid JSON", 400);
  }
}

export function createAtlasBridge(config, { fetcher = globalThis.fetch, dataDir = "./atlas-receipts-data" } = {}) {
  if (!config?.webhookSecret || !config?.operatorSecret) throw new Error("Both access secrets are required");
  if (!config?.paperlessToken || !config?.requiredTagId || !config?.paperlessInstance) throw new Error("Paperless configuration required");
  if (!config?.openLyricModel || !config?.openLyricToken) throw new Error("Open Lyric configuration required");
  if (!config?.receiptsApiKey || !config?.receiptsUrl) throw new Error("Receipts import credentials required");
  if (config.finalizationMode !== undefined && !["off", "strict"].includes(config.finalizationMode)) {
    throw new Error("Unknown finalizationMode: " + String(config.finalizationMode));
  }
  // Destination tag (requiredTagId) is the sole release signal; no separate
  // release tag exists. config.releaseTagId, when present, is ignored.
  // Native ID21 (ReTagged) carries no authority; its retirement is a
  // separate destructive approval outside this change.
  for (const [key, min] of [["finalizationMaxAttempts", 1], ["finalizationIntervalMs", 0], ["finalizationStableReads", 2]]) {
    const v = config[key];
    if (v !== undefined && (!Number.isSafeInteger(v) || v < min)) {
      throw new Error("Invalid " + key + ": " + String(v));
    }
  }
  mkdirSync(dataDir, { recursive: true, mode: 0o700 });
  // These are the existing native Atlas Hold/Clone tag IDs, not new gates.
  if (Number(config.requiredTagId) === 14) {
    config = { ...config, blockedTagIds: [...new Set([...(config.blockedTagIds ?? []), 28, 29])] };
  }
  const active = new Set();

  // The authorized destination tag is assigned upstream after OCR is ready.
  // Strict mode verifies the released Paperless revision before any model call.
  // Unproven or changed revisions fail closed without extraction.
  // HealthLog-style native handoff: the destination tag is the release
  // signal. The bridge has no owner/audit-history permissions. ReHarvest
  // owns actor authorization and pre-release review; the receiver owns
  // revision consistency, protected exclusions and idempotent import.
  async function admitRelease(docId, initial, proven, gateEvidence) {
    const current = await getPaperlessText(fetcher, config, docId);
    if (!sameSourceRevision(initial, proven) || !sameSourceRevision(proven, current)) {
      const old = getState(dataDir, docId);
      setState(dataDir, docId, {
        ...(old ?? { paperlessId: docId }),
        status: "RELEASE_UNPROVEN_REVIEW",
        reason: "Source revision changed or is unavailable during finalization; review and release the current source",
        finalization: { mode: config.finalizationMode ?? "off", ok: true, evidence: gateEvidence ?? null }
      });
      return { ok: false };
    }
    return { ok: true, binding: { method: "authenticated-destination-tag" } };
  }

  async function runFinalizationGate(id, source, admit = null) {
    const gate = await verifyFinalOcr(fetcher, config, source);
    if (gate.ok) {
      // Draft from the PROVEN revision, never the pre-gate fetch.
      const proven = gate.source ?? source;
      // Native admission runs AFTER the wait on purpose: the bound revision is
      // the one that would actually be extracted, closing mid-wait races.
      let binding = null;
      if (admit) {
        const a = await admit(proven, gate.evidence ?? null);
        if (!a.ok) return null;
        binding = a.binding ?? null;
      }
      return {
        parsed: sanitizePurchaseDraft(await extractUsingOpenLyric(fetcher, config, proven.content)),
        evidence: gate.evidence ?? null,
        proven, binding
      };
    }
    const old = getState(dataDir, id);
    setState(dataDir, id, {
      ...(old ?? { paperlessId: id }),
      status: gate.revisionChanged ? "RELEASE_UNPROVEN_REVIEW" : "REVIEW_REQUIRED",
      reason: gate.reason.slice(0, 160),
      finalization: { mode: gate.mode, ok: false }
    });
    return null;
  }

  async function processDocument(id) {
    if (active.has(id)) return;
    active.add(id);
    try {
      const previous = getState(dataDir, id);
      let source = await getPaperlessText(fetcher, config, id);
      if (previous?.receiptId) {
        const state = source.contentHash === previous.contentHash
          ? { ...previous, status: "IMPORTED" }
          // Source changed after import: force re-review and void the payment
          // confirmation, which described the pre-change source and draft.
          : { ...previous, status: "SOURCE_CHANGED_REVIEW", reason: "Paperless OCR text changed after import: re-check before relying on the link", confirmedFireflyTransactionId: null, confirmedAt: null };
        setState(dataDir, id, { ...state, sourceStatus: markAvailable() });
        return;
      }
      if (previous?.draft && sameSourceRevision(source, previous)) {
        // Same OCR revision: never auto-clear a review state or discard corrections.
        if (previous.status === "RELEASE_STALE_REVIEW") {
          // Source reverted to the released revision the draft was built from:
          // the release observation covers this revision again. Restore the
          // held draft without a new model call.
          setState(dataDir, id, {
            ...previous,
            status: (previous.problems ?? []).length ? "REVIEW_REQUIRED" : "READY_FOR_REVIEW",
            reason: undefined,
            sourceStatus: markAvailable()
          });
        }
        return;
      }
      if (previous?.draft && previous?.reviewerEdited) {
        // OCR changed after a reviewer corrected the draft: keep the corrections and
        // force re-review against the new text instead of silently replacing them.
        // Any payment confirmation is void: it was checked against the old source.
        setState(dataDir, id, {
          ...previous,
          status: "SOURCE_CHANGED_REVIEW",
          sourceStatus: markAvailable(),
          reason: "Paperless OCR text changed after review: saved corrections need re-check",
          confirmedFireflyTransactionId: null,
          confirmedAt: null
        });
        return;
      }
      if (previous?.draft && !previous?.reviewerEdited) {
        // CURRENT-SOURCE release before extraction: the release observed for
        // the drafted revision does NOT transfer to a changed source, even
        // when the release tag lingers on the document (tags are
        // document-level; native Paperless carries no tag-to-version binding).
        // Hold the new revision with zero model calls. A human adopts the
        // current source via the correction path after native review; a
        // revert to the released revision auto-resolves above.
        setState(dataDir, id, {
          ...previous,
          status: "RELEASE_STALE_REVIEW",
          sourceStatus: markAvailable(),
          reason: "Paperless source changed after release observation (prior " + String(previous.contentHash).slice(0, 12) + " now " + String(source.contentHash).slice(0, 12) + "): release does not transfer; no model call made",
          confirmedFireflyTransactionId: null,
          confirmedAt: null
        });
        return;
      }
      // Admission for first extraction: when a separately authorized release
      // destination tag is configured, native release binding gates extraction (the gate
      // above is reachable only with no previously extracted revision:
      // same-hash re-fires return early, changed revisions are held above).
      // The admit callback runs after the finalization wait so the bound
      // revision is the proven one.
      const gated = await runFinalizationGate(id, source, (proven, gateEvidence) => admitRelease(id, source, proven, gateEvidence));
      if (!gated) return;
      const parsed = gated.parsed;
      // Qualify and store the PROVEN revision: the gate may have waited
      // through OCR changes, so the pre-gate fetch is superseded here.
      source = gated.proven ?? source;
      const qualified = qualifyItemization(parsed, source.content);
      // Routes come from the PROVEN revision's tags, re-checked on every
      // re-fetch; a blocked tag throws here before any model call or draft.
      const routes = classifyRoutes(source.tags, config);
      const state = {
        paperlessId: id, contentHash: source.contentHash, modified: source.modified, versions: source.versions,
        sourceStatus: markAvailable(),
        status: qualified.ok ? "READY_FOR_REVIEW" : "REVIEW_REQUIRED",
        problems: qualified.problems, routes,
        // Record the handoff method; source revision and tags are re-read before import.
        releaseBinding: gated.binding ?? null,
        draftRevision: (previous?.draftRevision ?? 0) + 1,
        reviewerEdited: false,
        finalization: { mode: config.finalizationMode ?? "off", ok: true, evidence: gated.evidence },
        // No original PDF, card number, bank details or full OCR text stored here.
        draft: parsed
      };
      // Auto-import remains disabled by default. It requires an explicit config
      // opt-in AND all independent OCR evidence/decimal checks to pass.
      // Firefly is not modified in either mode.
      if (config.autoImport === true && qualified.ok) {
        // Extraction can take time. Never write a changed or revoked source.
        const current = await getPaperlessText(fetcher, config, id);
        if (!sameSourceRevision(source, current)) {
          setState(dataDir, id, { ...state, status: "RELEASE_STALE_REVIEW", reason: "Source revision changed during extraction; review required" });
          return;
        }
        const saved = await importQualifiedReceipt(fetcher, config, qualified, id);
        state.status = "IMPORTED";
        state.receiptId = saved.receiptId;
      }
      setState(dataDir, id, state);
    } catch (error) {
      const old = getState(dataDir, id);
      const tier = await classifySourceFailure(fetcher, config, id, error);
      setState(dataDir, id, {
        ...(old ?? { paperlessId: id }),
        status: old?.receiptId ? "SOURCE_CHANGED_REVIEW" : "REVIEW_REQUIRED",
        sourceStatus: tier,
        reason: error instanceof Error ? error.message.slice(0, 160) : "Processing failed"
      });
    } finally {
      active.delete(id);
    }
  }

  async function importReviewed(id, expectedRevision, approval = null, specialistSelection = null) {
    // Serialize concurrent approvals: the first import wins, the second sees the
    // recorded receiptId (idempotent replay) instead of posting a duplicate.
    if (active.has(id)) return { status: 409, result: { error: "Processing still active: retry shortly" } };
    const saved = getState(dataDir, id);
    if (!saved?.draft || !saved?.contentHash) return { status: 404, result: { error: "Draft not found" } };
    if (saved.receiptId) return { status: 200, result: { status: "IMPORTED", receiptId: saved.receiptId } };
    if (saved.status === "SOURCE_CHANGED_REVIEW") {
      return { status: 409, result: { error: "Source changed after review: correct the draft and save before approving", reason: saved.reason } };
    }
    if (expectedRevision !== undefined && expectedRevision !== (saved.draftRevision ?? null)) {
      return { status: 409, result: { error: "Draft changed since review: reload the draft before approving" } };
    }
    active.add(id);
    try {
      // Re-read the CURRENT source before importing: tags (required, release,
      // protected) plus immutable revision. A revoked release or changed OCR
      // between draft and approval must fail closed with no destination write.
      let current;
      try {
        current = await getPaperlessText(fetcher, config, id);
      } catch (error) {
        const tier = await classifySourceFailure(fetcher, config, id, error);
        setState(dataDir, id, { ...saved, status: "SOURCE_CHANGED_REVIEW", sourceStatus: tier, reason: "Paperless source or release changed before import: reprocess before saving", confirmedFireflyTransactionId: null, confirmedAt: null });
        return { status: 409, result: { error: "Paperless source or release changed: reprocess before saving" } };
      }
      if (!sameSourceRevision(current, saved)) {
        // The checked source is gone: block the import and void any payment
        // confirmation checked against the old revision.
        setState(dataDir, id, { ...saved, status: "SOURCE_CHANGED_REVIEW", sourceStatus: markAvailable(), reason: "OCR revision changed before import", confirmedFireflyTransactionId: null, confirmedAt: null });
        return { status: 409, result: { error: "Paperless OCR revision changed: reprocess before saving" } };
      }
      // The native source read above enforces the current destination tag,
      // protected exclusions, and the exact observed OCR/file revision.
      const qualified = qualifyItemization(saved.draft, current.content);
      // Confidence is advisory. Legacy attestation compatibility remains;
      // structural findings never become importable by review approval.
      const decision = authorizeImport(qualified, approval, {
        ocrText: current.content,
        draftRevision: String(saved.draftRevision ?? ""),
        proposer: saved.proposedBy ?? null
      });
      if (!decision.ok) return { status: 422, result: { error: "Itemization requires correction", problems: decision.problems } };
      const result = await importQualifiedReceipt(fetcher, config, qualified, id, decision.attestation);
      // Routes refresh from the approval-time re-fetch: a retag between review
      // and approval is honored, and a revoked tag already threw above.
      const routes = classifyRoutes(current.tags, config);
      // Specialist dispatch is staged and OFF by default. It runs only after
      // the human-approved import, binds to the approved revision, records
      // its outcome separately, and never alters IMPORTED or Firefly state.
      let specialist = null;
      if (config.specialistDispatch?.enabled === true) {
        try {
          const { dispatchSpecialists } = await import("./specialist-dispatch.mjs");
          specialist = await dispatchSpecialists(fetcher, config.specialistDispatch, { routes }, qualified, { paperlessId: id, contentHash: saved.contentHash }, { ...(approval ?? {}), ...(specialistSelection ?? {}) });
        } catch (error) {
          specialist = { dispatched: false, error: error?.message ?? String(error) };
        }
      }
      setState(dataDir, id, { ...saved, status: "IMPORTED", receiptId: result.receiptId, routes, specialist, sourceStatus: markAvailable() });
      return { status: 200, result: { status: "IMPORTED", receiptId: result.receiptId } };
    } finally {
      active.delete(id);
    }
  }

  async function matchReviewed(id, sourceAccountId) {
    const saved = getState(dataDir, id);
    if (!saved?.draft || !saved?.contentHash) return { status: 404, result: { error: "Draft not found" } };
    const current = await getPaperlessText(fetcher, config, id);
    if (current.contentHash !== saved.contentHash) {
      // The checked source is gone: block the match, mark the draft stale for
      // re-review, and void any payment confirmation checked against the old revision.
      setState(dataDir, id, { ...saved, status: "SOURCE_CHANGED_REVIEW", reason: "OCR source revision changed: re-check before matching", confirmedFireflyTransactionId: null, confirmedAt: null });
      return { status: 409, result: { error: "OCR source revision changed" } };
    }
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

  async function confirmMatch(id, fireflyTransactionId, fireflySourceAccountId) {
    // Durable, review-confirmed payment association stored inside the bridge only.
    // Never writes to Firefly: the suggestion must already be a unique PROPOSED_MATCH
    // for exactly this transaction, otherwise confirmation is rejected.
    if (typeof fireflyTransactionId !== "string" || fireflyTransactionId.length < 1 || fireflyTransactionId.length > 80) {
      return { status: 400, result: { error: "Confirmed Firefly transaction ID is required" } };
    }
    // Snapshot before the async Firefly work: a reviewer save or OCR change that
    // lands mid-flight must not be confirmed against (race guard, checked below).
    const before = getState(dataDir, id);
    const beforeRevision = before?.draftRevision ?? null;
    const beforeHash = before?.contentHash ?? null;
    const matched = await matchReviewed(id, String(fireflySourceAccountId ?? ""));
    if (matched.status !== 200) return matched;
    const suggestion = matched.result;
    if (suggestion.status !== "PROPOSED_MATCH" || suggestion.fireflyTransactionId !== fireflyTransactionId) {
      return { status: 409, result: { error: "Confirmed payment does not equal the current unique suggestion: re-check before linking", suggestion } };
    }
    // Re-read AFTER Firefly responded: if the revision or source changed while
    // checking, the suggestion describes a stale draft - conflict, never link.
    // No awaits between this re-read and setState below, so the store is atomic.
    const saved = getState(dataDir, id);
    if ((saved?.draftRevision ?? null) !== beforeRevision || (saved?.contentHash ?? null) !== beforeHash) {
      return { status: 409, result: { error: "Receipt changed while checking the payment: re-check before linking" } };
    }
    // Replay of the same confirmation is idempotent. A different receipt
    // claiming the same payment is rejected: one-to-one, globally enforced.
    if (saved?.confirmedFireflyTransactionId === fireflyTransactionId) {
      return { status: 200, result: { status: "MATCH_CONFIRMED", fireflyTransactionId } };
    }
    const other = findOtherClaimant(dataDir, fireflyTransactionId, id);
    if (other !== null) {
      return { status: 409, result: { error: "Firefly payment already linked to another receipt: review before linking", claimedByPaperlessId: other } };
    }
    setState(dataDir, id, {
      ...saved,
      confirmedFireflyTransactionId: fireflyTransactionId,
      confirmedAt: new Date().toISOString()
    });
    return { status: 200, result: { status: "MATCH_CONFIRMED", fireflyTransactionId } };
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
        const body = await jsonBody(request);
        rejectReleaseClaim(body);
        const id = docIdOf(body);
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
      if (request.method === "GET" && path === "/drafts") {
        // Bounded operator queue; no originals or full OCR are in this state.
        const ids = readdirSync(dataDir)
          .filter(name => /^paperless-[1-9][0-9]*[.]json$/.test(name))
          .map(name => Number(name.slice(10, -5)))
          .filter(Number.isSafeInteger)
          .sort((a, b) => b - a)
          .slice(0, 100);
        const drafts = ids.map(id => {
          const state = getState(dataDir, id);
          return state && {
            paperlessId: id, status: state.status, modified: state.modified,
            problems: state.problems ?? [], receiptId: state.receiptId ?? null, sourceStatus: state.sourceStatus ?? null,
            routes: state.routes ?? [], specialist: state.specialist ?? null,
            draftRevision: state.draftRevision ?? null,
            confirmedFireflyTransactionId: state.confirmedFireflyTransactionId ?? null,
            merchant: state.draft?.merchant ?? null,
            date: state.draft?.date ?? null,
            total: state.draft?.total ?? null
          };
        }).filter(Boolean);
        return safeJson(response, 200, { drafts, limit: 100 });
      }
      const match = /^\/drafts\/([1-9][0-9]*)(?:\/(import|match|confirm-match))?$/.exec(path);
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
        if (active.has(id)) return safeJson(response, 409, { error: "Processing still active: retry shortly" });
        const edited = await jsonBody(request, 100000);
        // Compare-and-swap: the saver must name the revision it edited, so a
        // second reviewer's save cannot silently overwrite the first.
        const expectedSave = edited?.expectedRevision;
        if (!Number.isSafeInteger(expectedSave) || expectedSave < 1) {
          return safeJson(response, 400, { error: "Expected draft revision is required to save corrections" });
        }
        if (expectedSave !== (previous.draftRevision ?? null)) {
          return safeJson(response, 409, { error: "Draft changed since you loaded it: reload before saving", draftRevision: previous.draftRevision ?? null });
        }
        // RELEASE_STALE adoption: the human reviewed the CURRENT source
        // natively and corrects against it, so the save adopts the current
        // revision as the reviewed baseline. The re-read enforces the current
        // required/release/blocked tags; the human's correction is the
        // freshness attestation the bridge cannot observe natively.
        let baseline = previous;
        if (["RELEASE_STALE_REVIEW", "SOURCE_CHANGED_REVIEW"].includes(previous.status)) {
          let current;
          try {
            current = await getPaperlessText(fetcher, config, id);
          } catch (error) {
            return safeJson(response, 409, { error: "Current source or release changed: re-check before saving" });
          }
          baseline = { ...previous, contentHash: current.contentHash, modified: current.modified, versions: current.versions ?? null };
        }
        const draftRevision = (baseline.draftRevision ?? 0) + 1;
        // Still checked against immutable OCR evidence at import time.
        // Any edit voids a prior payment confirmation: it was checked against
        // the pre-edit draft, so the reviewer must re-check before approval.
        const confirmationInvalidated = baseline.confirmedFireflyTransactionId != null;
        setState(dataDir, id, { ...baseline, draft: sanitizePurchaseDraft(edited), status: "READY_FOR_REVIEW", problems: [], reviewerEdited: true, draftRevision, confirmedFireflyTransactionId: null, confirmedAt: null });
        return safeJson(response, 200, { status: "DRAFT_UPDATED", draftRevision, confirmationInvalidated });
      }
      if (request.method === "POST" && match[2] === "import") {
        // Explicit revision required: the approver must name the exact draft
        // revision reviewed. No silent legacy path: an approval without a
        // named revision is rejected instead of importing whatever is newest.
        const importBody = await jsonBody(request, 1000);
        if (!Number.isSafeInteger(importBody?.expectedRevision) || importBody.expectedRevision < 1) {
          return safeJson(response, 400, { error: "Expected draft revision is required to approve an import" });
        }
        // Reviewer identity comes from the authenticated API layer's stamped
        // header, never from the request body; the body supplies only the
        // binding evidence (timestamp, OCR digest, draft revision).
        // Reviewer identity is stamped into the body by the authenticated API
        // layer; the header is only a fallback for direct operator calls.
        const review = importBody?.review;
        const reviewer = (typeof review?.reviewer === "string" ? review.reviewer.trim() : "") ||
          String(request.headers["x-atlas-reviewer"] ?? "").trim();
        const approval = review && typeof review === "object"
          ? { reviewer, approvedAt: review.approvedAt, ocrSha256: review.ocrSha256, draftRevision: review.draftRevision }
          : null;
        // Operator-selected specialist routing rides alongside the approval
        // (never inside the attestation): the reviewer picks the vehicle and
        // service category in the approval dialog. Absent selections hold
        // dispatch; nothing is ever invented.
        const specialistSelection = {
          selectedVehicleVin: typeof importBody?.selectedVehicleVin === "string" ? importBody.selectedVehicleVin : null,
          serviceCategory: typeof importBody?.serviceCategory === "string" ? importBody.serviceCategory : null
        };
        const result = await importReviewed(id, importBody.expectedRevision, approval, specialistSelection);
        return safeJson(response, result.status, result.result);
      }
      if (request.method === "POST" && match[2] === "match") {
        const body = await jsonBody(request);
        const result = await matchReviewed(id, String(body.fireflySourceAccountId ?? ""));
        return safeJson(response, result.status, result.result);
      }
      if (request.method === "POST" && match[2] === "confirm-match") {
        const body = await jsonBody(request, 1000);
        const result = await confirmMatch(id, body?.fireflyTransactionId, body?.fireflySourceAccountId);
        return safeJson(response, result.status, result.result);
      }
      return safeJson(response, 405, { error: "Method not allowed" });
    } catch (error) {
      // No tokens, OCR, model output, or payment data in errors/logs.
      const reason = error instanceof Error ? error.message : "Unexpected error";
      const status = error instanceof Error && Number.isSafeInteger(error.statusCode) ? error.statusCode : 422;
      return safeJson(response, status, { error: reason.slice(0, 160) });
    }
  });

  // Pending work is intentionally not auto-replayed after an unclean restart.
  // Paperless's native manual workflow and re-tagging can retrigger an ID safely.
  // Avoid claiming unattended durable recovery without a proper queue.
  return { server, processDocument, importReviewed, matchReviewed, confirmMatch, state: id => getState(dataDir, id) };
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
    releaseTagId: undefined, // retired: ignored; native ID21 retirement needs separate approval
    grocyTagId: process.env.ATLAS_PAPERLESS_GROCY_TAG_ID ? Number(process.env.ATLAS_PAPERLESS_GROCY_TAG_ID) : null,
    myGarageTagId: process.env.ATLAS_PAPERLESS_MYGARAGE_TAG_ID ? Number(process.env.ATLAS_PAPERLESS_MYGARAGE_TAG_ID) : null,
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
    autoImport: process.env.ATLAS_AUTO_IMPORT_ENABLED === "1",
    finalizationMode: process.env.ATLAS_FINALIZATION_MODE === "strict" ? "strict" : "off",
    finalizationMaxAttempts: Number(process.env.ATLAS_FINALIZATION_MAX_ATTEMPTS || 6),
    finalizationIntervalMs: Number(process.env.ATLAS_FINALIZATION_INTERVAL_MS || 5000),
    finalizationStableReads: Number(process.env.ATLAS_FINALIZATION_STABLE_READS || 2)
  };
  const bind = process.env.ATLAS_BRIDGE_BIND || "127.0.0.1";
  const port = Number(process.env.ATLAS_BRIDGE_PORT || 8675);
  if (!Number.isSafeInteger(port) || port < 1 || port > 65535) throw new Error("Invalid port");
  const { server } = createAtlasBridge(config, { dataDir: process.env.ATLAS_BRIDGE_DATA_DIR || "./atlas-receipts-data" });
  server.listen(port, bind, () => console.log("Atlas receipt bridge listening (review-only; no Firefly write access)"));
}
