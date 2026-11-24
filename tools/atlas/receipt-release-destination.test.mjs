// Destination-tag release binding — focused RED/GREEN (synthetic only).
// The destination tag (e.g. Tracer 14), assigned only by an authorized
// Shadow/human after review, is the SOLE release signal. No separate release
// tag exists. Stale tags (ID21 ReTagged/Shadow Route or any other name) never
// authorize; history matches by stable numeric id only. Blocked Hold/Clone
// deny; missing config denies closed. No live access, no network.
import test from "node:test";
import assert from "node:assert/strict";
import { evaluateReleaseBinding } from "./receipt-bridge-core.mjs";

const T0 = "2026-11-20T22:15:46Z";
const T2 = "2026-11-22T09:00:00Z";
const versionsA = [{ added: T0, checksum: null, is_root: true }];
const add = (objects, ts) => ({ action: "update", tagsOp: "add", tagsObjects: objects, ts, actorId: 2 });

function decide(over = {}) {
  return evaluateReleaseBinding({
    currentTags: [14, 21],
    history: [add([14], T2)],
    versions: versionsA,
    bridgeContentHash: "synthetic",
    authorizedReleasers: [2],
    requiredTagId: 14,
    blockedTagIds: [28],
    ...over,
  });
}

test("authorized destination add authorizes", () => {
  const r = decide();
  assert.equal(r.allow, true);
});

test("stale ID21 add alone never authorizes", () => {
  const r = decide({ currentTags: [14, 21], history: [add([21], T2)] });
  assert.equal(r.allow, false);
});

test("historic ReTagged name never authorizes", () => {
  const r = decide({ history: [add(["ReTagged"], T2)] });
  assert.equal(r.allow, false);
});

test("missing destination config denies closed", () => {
  const r = decide({ requiredTagId: undefined });
  assert.equal(r.allow, false);
  assert.match(r.reason, /destination tag unconfigured/i);
});

test("destination present without authorized history add denies", () => {
  const r = decide({ currentTags: [14], history: [] });
  assert.equal(r.allow, false);
});

test("stale tag without destination denies", () => {
  const r = decide({ currentTags: [21], history: [add([21], T2)] });
  assert.equal(r.allow, false);
});

test("configured Hold blocks even with destination present", () => {
  const r = decide({ currentTags: [14, 21, 28] });
  assert.equal(r.allow, false);
});

test("configured Clone blocks even with destination present", () => {
  const r = decide({ currentTags: [14, 21, 99], blockedTagIds: [28, 99] });
  assert.equal(r.allow, false);
});
