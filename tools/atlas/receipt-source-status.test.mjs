// Source availability probe tiers — synthetic fetchers only. No live access.
// AVAILABLE needs a current exact-id retrieve; RETIRED needs positive trash
// evidence (id + deleted_at); 401/403 stay DENIED; everything else UNKNOWN.
import test from "node:test";
import assert from "node:assert/strict";
import { probeSourceStatus } from "./receipt-bridge-core.mjs";

const base = { paperlessUrl: "https://paperless.test", paperlessToken: "synthetic" };
const doc = { id: 9501, content: "x".repeat(30) };
const okDoc = () => new Response(JSON.stringify(doc), { status: 200 });
const notFound = () => new Response(JSON.stringify({ detail: "Not found." }), { status: 404 });

test("current retrieve proves AVAILABLE", async () => {
  const r = await probeSourceStatus(async () => okDoc(), base, 9501);
  assert.equal(r.status, "AVAILABLE");
});

test("trash hit with deleted_at proves RETIRED", async () => {
  const trash = { results: [{ id: 9501, deleted_at: "2026-11-20T00:00:00Z" }], next: null };
  const f = async (url) => String(url).includes("/api/trash")
    ? new Response(JSON.stringify(trash), { status: 200 }) : notFound();
  const r = await probeSourceStatus(f, { ...base, paperlessTrashPath: "/api/trash/" }, 9501);
  assert.equal(r.status, "RETIRED");
});

test("bare 404 without trash path stays UNKNOWN, never retired", async () => {
  const r = await probeSourceStatus(async () => notFound(), base, 9501);
  assert.equal(r.status, "UNKNOWN");
});

test("404 with trash miss stays UNKNOWN", async () => {
  const trash = { results: [{ id: 1234, deleted_at: "2026-11-20T00:00:00Z" }], next: null };
  const f = async (url) => String(url).includes("/api/trash")
    ? new Response(JSON.stringify(trash), { status: 200 }) : notFound();
  const r = await probeSourceStatus(f, { ...base, paperlessTrashPath: "/api/trash/" }, 9501);
  assert.equal(r.status, "UNKNOWN");
});

test("401 and 403 stay DENIED", async () => {
  for (const code of [401, 403]) {
    const r = await probeSourceStatus(
      async () => new Response("denied", { status: code }), base, 9501);
    assert.equal(r.status, "DENIED");
  }
});

test("network failure and trash failure stay UNKNOWN", async () => {
  const r1 = await probeSourceStatus(async () => { throw new Error("socket hang up"); }, base, 9501);
  assert.equal(r1.status, "UNKNOWN");
  const f = async (url) => String(url).includes("/api/trash")
    ? new Response("err", { status: 500 }) : notFound();
  const r2 = await probeSourceStatus(f, { ...base, paperlessTrashPath: "/api/trash/" }, 9501);
  assert.equal(r2.status, "UNKNOWN");
});
