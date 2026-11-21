import assert from "node:assert/strict";
import test from "node:test";
import { resolveVehicle, toFuelRecord, postFuelRecord, toServiceVisit, postServiceVisit } from "./specialist-mygarage.mjs";

const vehicles = [
  { vin: "VINAAA11111111111", label: "Camry" },
  { vin: "VINBBB22222222222", label: "RAV4" }
];

test("exact vehicle hint resolves", () => {
  assert.equal(resolveVehicle(vehicles, "camry").vin, "VINAAA11111111111");
});

test("ambiguous vehicles without a hint throw for reviewer selection", () => {
  assert.throws(() => resolveVehicle(vehicles, null), /ambiguous/i);
});

test("unknown hint throws instead of inventing a vehicle", () => {
  assert.throws(() => resolveVehicle(vehicles, "cybertruck"), /no vehicle/i);
});

const qualified = {
  merchant: "SHELL", date: "2026-11-20",
  items: [{ description: "REGULAR", quantity: "12.5", unitOfMeasure: "gal", total: "41.25" }],
  total: "41.25"
};

test("fuel record converts gallons to liters and carries the source tag", () => {
  const rec = toFuelRecord(qualified, { vin: "VINAAA11111111111" }, { paperlessId: 9401, contentHash: "abc123" });
  assert.equal(rec.vin, "VINAAA11111111111");
  assert.equal(rec.date, "2026-11-20");
  assert.ok(Math.abs(rec.liters - 47.3176) < 0.001);
  assert.match(rec.notes ?? "", /tracer:paperless:9401:abc123/);
});

test("miles convert to kilometers; missing odometer is omitted not invented", () => {
  const withOdo = toFuelRecord(qualified, { vin: "VINAAA11111111111", odometer: "120000", odometerUnit: "mi" }, { paperlessId: 9401, contentHash: "abc123" });
  assert.ok(Math.abs(withOdo.odometer_km - 193120.8) < 0.01);
  const withoutOdo = toFuelRecord(qualified, { vin: "VINAAA11111111111" }, { paperlessId: 9401, contentHash: "abc123" });
  assert.equal("odometer_km" in withoutOdo, false);
});

test("missing fuel quantity throws instead of posting a guess", () => {
  const bad = { ...qualified, items: [{ description: "CAR WASH", quantity: "1", unit: "each", total: "9.00" }] };
  assert.throws(() => toFuelRecord(bad, { vin: "VINAAA11111111111" }, { paperlessId: 9401, contentHash: "abc123" }), /fuel quantity/i);
});

function stubFetch(existing) {
  const calls = [];
  const fetcher = async (url, options = {}) => {
    calls.push({ url, method: options.method ?? "GET", body: options.body });
    if ((options.method ?? "GET") === "GET" && url.includes("/fuel")) {
      return new Response(JSON.stringify(existing), { status: 200, headers: { "content-type": "application/json" } });
    }
    return new Response(JSON.stringify({ id: "fuel-1" }), { status: 201, headers: { "content-type": "application/json" } });
  };
  return { fetcher, calls };
}

test("duplicate source tag reconciles without a second POST", async () => {
  const tagged = [{ notes: "tracer:paperless:9401:abc123" }];
  const { fetcher, calls } = stubFetch(tagged);
  const out = await postFuelRecord(fetcher, "https://mygarage.test", "token", "VINAAA11111111111", { date: "2026-11-20", liters: 47.318 }, { paperlessId: 9401, contentHash: "abc123" });
  assert.equal(out.deduplicated, true);
  assert.equal(calls.filter(c => c.method === "POST").length, 0);
});

test("new fuel record posts once with the auth token", async () => {
  const { fetcher, calls } = stubFetch([]);
  const out = await postFuelRecord(fetcher, "https://mygarage.test", "token", "VINAAA11111111111", { date: "2026-11-20", liters: 47.318 }, { paperlessId: 9401, contentHash: "abc123" });
  assert.equal(out.deduplicated, false);
  const posts = calls.filter(c => c.method === "POST");
  assert.equal(posts.length, 1);
  assert.match(posts[0].url, /\/api\/vehicles\/VINAAA11111111111\/fuel$/);
});

function stubServiceFetch(existing) {
  const calls = [];
  const fetcher = async (url, options = {}) => {
    calls.push({ url, method: options.method ?? "GET", body: options.body });
    if ((options.method ?? "GET") === "GET" && url.includes("/service-visits")) {
      return new Response(JSON.stringify(existing), { status: 200, headers: { "content-type": "application/json" } });
    }
    if (url.includes("/line-items")) {
      return new Response(JSON.stringify({ id: "line-1" }), { status: 201, headers: { "content-type": "application/json" } });
    }
    return new Response(JSON.stringify({ id: "visit-1" }), { status: 201, headers: { "content-type": "application/json" } });
  };
  return { fetcher, calls };
}

const serviceQualified = {
  merchant: "J.L. FREY", date: "2026-11-18",
  items: [{ description: "OIL CHANGE", quantity: "1", unit: "each", total: "59.99" }],
  total: "59.99"
};

test("service visit carries category, source tag and line items", () => {
  const v = toServiceVisit(serviceQualified, { vin: "VINAAA11111111111", odometer: "75000", odometerUnit: "mi" }, { paperlessId: 9501, contentHash: "def456" }, { category: "Maintenance" });
  assert.equal(v.visit.service_category, "Maintenance");
  assert.equal(v.visit.date, "2026-11-18");
  assert.match(v.visit.notes ?? "", /tracer:paperless:9501:def456/);
  assert.ok(Math.abs(v.visit.odometer_km - 120700.5) < 0.01);
  assert.deepEqual(v.lineItems, [{ description: "OIL CHANGE", category: "Maintenance", cost: 59.99 }]);
});

test("unknown service category throws for reviewer selection", () => {
  assert.throws(() => toServiceVisit(serviceQualified, { vin: "VINAAA11111111111" }, { paperlessId: 9501, contentHash: "def456" }, { category: "Warp Drive" }), /category/i);
});

test("duplicate service visit reconciles without POSTs", async () => {
  const { fetcher, calls } = stubServiceFetch([{ notes: "tracer:paperless:9501:def456" }]);
  const out = await postServiceVisit(fetcher, "https://mygarage.test", "token", "VINAAA11111111111",
    { date: "2026-11-18", service_category: "Maintenance", notes: "tracer:paperless:9501:def456" },
    [{ description: "OIL CHANGE", cost: 59.99 }], { paperlessId: 9501, contentHash: "def456" });
  assert.equal(out.deduplicated, true);
  assert.equal(calls.filter(c => c.method === "POST").length, 0);
});
