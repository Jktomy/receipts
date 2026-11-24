// W25 native release binding — pure predicate tests (synthetic fixtures shaped
// like the W27-documented native contract: historyList entries + retrieve fields).
// No live access, no network, no bridge. Adapter parity rows mirror
// w27-release/adapter-logic.test.mjs; edge rows cover the fail-closed limits.
import test from "node:test";
import assert from "node:assert/strict";
import { evaluateReleaseBinding } from "./receipt-bridge-core.mjs";

const T0 = "2026-11-20T22:15:46Z"; // version A added + 21 added (in-story)
const T0b = "2026-11-20T22:15:47Z"; // one second after T0 (strictly ordered)
const T1 = "2026-11-21T10:00:00Z"; // content changed -> version B
const T2 = "2026-11-22T09:00:00Z"; // fresh authorized re-release

const add21 = (ts, actorId = 2, action = "update") =>
  ({ action, tagsOp: "add", tagsObjects: [21], ts, actorId });
const remove21 = (ts, actorId = 2) =>
  ({ action: "update", tagsOp: "remove", tagsObjects: [21], ts, actorId });
const versionsA = [{ added: T0, checksum: null, is_root: true }];

function decide(over = {}) {
  return evaluateReleaseBinding({
    currentTags: [14, 21],
    history: [],
    versions: versionsA,
    bridgeContentHash: "synthetic",
    authorizedReleasers: [2],
    requiredTagId: 14,
    blockedTagIds: [17, 4, 18, 20],
    ...over,
  });
}

test("release for A denies B while 21 stays (no re-authorization)", () => {
  const r = decide({
    versions: [{ added: T1, checksum: null, is_root: true }],
    documentModified: T1,
    history: [add21(T0)],
  });
  assert.equal(r.allow, false);
  assert.match(r.reason, /floor/);
});

test("fresh authorized 21-add after B allows B", () => {
  const r = decide({
    versions: [{ added: T1, checksum: null }],
    documentModified: T1,
    history: [add21(T0), add21(T2)],
  });
  assert.equal(r.allow, true);
  assert.equal(r.qualifyingAdds, 1);
});

test("later logged removal denies even with 21 re-present", () => {
  const r = decide({
    history: [add21(T0b), remove21(T1)],
  });
  assert.equal(r.allow, false);
  assert.match(r.reason, /removed/);
});

test("R20 HOLD: empty authorized set denies everything", () => {
  const r = decide({ authorizedReleasers: [], history: [add21(T2)] });
  assert.equal(r.allow, false);
  assert.match(r.reason, /HOLD/);
});

test("unauthorized actor denies", () => {
  const r = decide({ authorizedReleasers: [7], history: [add21(T2)] });
  assert.equal(r.allow, false);
  assert.match(r.reason, /floor/);
});

test("missing 21 on current tags denies (history alone insufficient)", () => {
  const r = decide({ currentTags: [14], history: [add21(T2)] });
  assert.equal(r.allow, false);
});

test("blocked tag denies despite authorized add", () => {
  const r = decide({ currentTags: [14, 21, 18], history: [add21(T2)] });
  assert.equal(r.allow, false);
  assert.match(r.reason, /excluded/);
});

test("missing history denies (not proof of absence)", () => {
  const r = decide({ history: null });
  assert.equal(r.allow, false);
});

test("empty versions denies (filtered retrieve is not proof of absence)", () => {
  const r = decide({ versions: [], history: [add21(T2)] });
  assert.equal(r.allow, false);
});

test("unparseable version timestamp denies", () => {
  const r = decide({ versions: [{ added: "not-a-time" }], history: [add21(T2)] });
  assert.equal(r.allow, false);
});

test("uninterpretable tag-touching entry denies (cannot exclude a 21 removal)", () => {
  const r = decide({
    history: [add21(T2), { action: "update", changes: { tags: { operation: "add" } }, ts: T2, actorId: 2 }],
  });
  assert.equal(r.allow, false);
  assert.match(r.reason, /uninterpretable/);
});

test("non-tag history entries are ignored", () => {
  const r = decide({
    history: [{ action: "update", title: "renamed", ts: T1, actorId: 9 }, add21(T2)],
  });
  assert.equal(r.allow, true);
});

test("ocrSettledAt term is live: add before settlement denies", () => {
  const r = decide({
    ocrSettledAt: T2,
    history: [add21(T1)],
  });
  assert.equal(r.allow, false);
});

test("exact checksum match selects the current row (revert restores releasability)", () => {
  // Content reverted to A (hash match selects row A); the authorizing add
  // strictly postdates A's version creation.
  const r = decide({
    bridgeContentHash: "aaa",
    versions: [
      { added: T0, checksum: "aaa" },
      { added: T1, checksum: "bbb" },
    ],
    history: [add21(T0b)],
  });
  assert.equal(r.allow, true);
  assert.equal(r.versionAdded, Date.parse(T0));
});

test("same-instant add does not authorize (no rounding earlier into equal)", () => {
  const r = decide({
    history: [add21(T1)],
    versions: [{ added: T1, checksum: null }],
  });
  assert.equal(r.allow, false);
  assert.match(r.reason, /floor/);
});

test("same-request tag add with content change denies (ambiguity fails closed)", () => {
  // One native entry both adds 21 and changes content at the same instant:
  // ordering cannot be established, so the add authorizes nothing.
  const r = decide({
    history: [{ action: "update", tagsOp: "add", tagsObjects: [21], ts: T2, actorId: 2, changes: { content: "re-ocr" } }],
  });
  assert.equal(r.allow, false);
  assert.match(r.reason, /floor/);
});

test("later audited content change without new version denies", () => {
  // OCR-only change: no new file version, but the audit log records a content
  // change after the authorizing add. The add predates current content.
  const r = decide({
    history: [add21(T0), { action: "update", changes: { content: "ocr-refresh" }, ts: T1, actorId: 2 }],
  });
  assert.equal(r.allow, false);
  assert.match(r.reason, /floor/);
});

test("unorderable content change denies", () => {
  const r = decide({
    history: [add21(T2), { action: "update", changes: { checksum: "x" }, actorId: 2 }],
  });
  assert.equal(r.allow, false);
  assert.match(r.reason, /unorderable content/);
});

test("remove at the same instant as the authorizing add denies", () => {
  const r = decide({
    history: [add21(T2), remove21(T2)],
  });
  assert.equal(r.allow, false);
  assert.match(r.reason, /removed/);
});

test("documented tag name matches 21", () => {
  const r = decide({
    currentTags: [14, "ReTagged"],
    history: [{ action: "update", tagsOp: "add", tagsObjects: ["ReTagged"], ts: T2, actorId: 2 }],
  });
  assert.equal(r.allow, true);
});

test("sub-millisecond ordering is respected exactly as parsed", () => {
  // Timestamps 1ms apart order deterministically; the earlier add does not
  // cover the later version. No rounding to the same instant.
  const r = decide({
    versions: [{ added: "2026-11-20T22:15:46.001Z", checksum: null }],
    history: [add21("2026-11-20T22:15:46.000Z")],
  });
  assert.equal(r.allow, false);
  assert.match(r.reason, /floor/);
  const ok = decide({
    versions: [{ added: "2026-11-20T22:15:46.000Z", checksum: null }],
    history: [add21("2026-11-20T22:15:46.001Z")],
  });
  assert.equal(ok.allow, true);
});

test("creation carrying 21 is not a release add", () => {
  const r = decide({
    history: [add21(T2, 2, "create")],
  });
  assert.equal(r.allow, false);
});
