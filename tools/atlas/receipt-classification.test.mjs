import assert from "node:assert/strict";
import test from "node:test";
import { classifyRoutes } from "./receipt-bridge-core.mjs";

const config = { requiredTagId: 14, grocyTagId: 15, myGarageTagId: 13, blockedTagIds: [18, 4, 17, 20] };

test("tracer-only receipt routes to tracer", () => {
  assert.deepEqual(classifyRoutes([14], config), ["tracer"]);
});

test("grocery receipt routes to tracer and grocy", () => {
  assert.deepEqual(classifyRoutes([14, 15], config), ["tracer", "grocy"]);
});

test("fuel receipt routes to tracer and mygarage", () => {
  assert.deepEqual(classifyRoutes([14, 13], config), ["tracer", "mygarage"]);
});

test("receipt with both specialist tags routes to all three", () => {
  assert.deepEqual(classifyRoutes([14, 15, 13], config), ["tracer", "grocy", "mygarage"]);
});

test("unrelated tags are ignored, tracer still routes", () => {
  assert.deepEqual(classifyRoutes([14, 12, 1], config), ["tracer"]);
});

test("missing tracer tag throws without routing", () => {
  assert.throws(() => classifyRoutes([15], config), /Required receipt-analysis tag absent/);
});

test("protected tags throw even with tracer present", () => {
  for (const blocked of [18, 4, 17, 20]) {
    assert.throws(() => classifyRoutes([14, blocked], config), /excluded sensitive tag/);
  }
});

test("protected tag beats specialist routing", () => {
  assert.throws(() => classifyRoutes([14, 15, 20], config), /excluded sensitive tag/);
});

test("non-array tags throw", () => {
  assert.throws(() => classifyRoutes(null, config), /tags/);
});

test("string tag IDs from the API coerce like numbers", () => {
  assert.deepEqual(classifyRoutes(["14", "15"], config), ["tracer", "grocy"]);
});

test("string protected tag still blocks", () => {
  assert.throws(() => classifyRoutes(["14", "20"], config), /excluded sensitive tag/);
});
