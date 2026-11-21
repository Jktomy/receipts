import assert from "node:assert/strict";
import test from "node:test";
import { dispatchSpecialists } from "./specialist-dispatch.mjs";

const source = { paperlessId: 9401, contentHash: "abc123def456" };
const fuelQualified = {
  merchant: "SHELL", date: "2026-11-20",
  items: [{ description: "REGULAR", quantity: "12.5", unit: "gal", total: "41.25" }],
  total: "41.25"
};

function stubMyGarage(existingFuel = [], existingVisits = []) {
  const calls = [];
  const fetcher = async (url, options = {}) => {
    calls.push({ url, method: options.method ?? "GET" });
    if ((options.method ?? "GET") === "GET" && url.includes("/service-visits")) {
      return new Response(JSON.stringify(existingVisits), { status: 200, headers: { "content-type": "application/json" } });
    }
    if ((options.method ?? "GET") === "GET" && url.includes("/fuel")) {
      return new Response(JSON.stringify(existingFuel), { status: 200, headers: { "content-type": "application/json" } });
    }
    if (url.includes("/line-items")) {
      return new Response(JSON.stringify({ id: "line-1" }), { status: 201, headers: { "content-type": "application/json" } });
    }
    if (url.includes("/fuel")) {
      return new Response(JSON.stringify({ id: "fuel-1" }), { status: 201, headers: { "content-type": "application/json" } });
    }
    return new Response(JSON.stringify({ id: "visit-1" }), { status: 201, headers: { "content-type": "application/json" } });
  };
  return { fetcher, calls };
}

test("dispatch disabled makes zero specialist calls", async () => {
  const { fetcher, calls } = stubMyGarage();
  const out = await dispatchSpecialists(fetcher, { enabled: false }, { routes: ["tracer", "mygarage"] }, fuelQualified, source, {});
  assert.equal(out.dispatched, false);
  assert.equal(calls.length, 0);
});

test("fuel dispatch posts once with reviewer-selected vehicle", async () => {
  const { fetcher, calls } = stubMyGarage();
  const out = await dispatchSpecialists(fetcher,
    { enabled: true, mygarage: { baseUrl: "https://mygarage.test", token: "token", vehicles: [{ vin: "VINAAA11111111111", label: "Camry" }] } },
    { routes: ["tracer", "mygarage"] }, fuelQualified, source,
    { selectedVehicleVin: "VINAAA11111111111" });
  assert.equal(out.dispatched, true);
  assert.equal(out.fuel.deduplicated, false);
  assert.equal(calls.filter(c => c.method === "POST").length, 1);
});

test("duplicate fuel reconciles without a second POST", async () => {
  const { fetcher, calls } = stubMyGarage([{ notes: "tracer:paperless:9401:abc123de" }]);
  const out = await dispatchSpecialists(fetcher,
    { enabled: true, mygarage: { baseUrl: "https://mygarage.test", token: "token", vehicles: [{ vin: "VINAAA11111111111", label: "Camry" }] } },
    { routes: ["tracer", "mygarage"] }, fuelQualified, source,
    { selectedVehicleVin: "VINAAA11111111111" });
  assert.equal(out.fuel.deduplicated, true);
  assert.equal(calls.filter(c => c.method === "POST").length, 0);
});

test("missing vehicle selection holds fuel dispatch without calls", async () => {
  const { fetcher, calls } = stubMyGarage();
  const out = await dispatchSpecialists(fetcher,
    { enabled: true, mygarage: { baseUrl: "https://mygarage.test", token: "token", vehicles: [{ vin: "VINAAA11111111111", label: "Camry" }] } },
    { routes: ["tracer", "mygarage"] }, fuelQualified, source, {});
  assert.equal(out.dispatched, false);
  assert.match(out.mygarage?.reason ?? "", /vehicle/i);
  assert.equal(calls.length, 0);
});

test("grocy route stays held with zero stock calls", async () => {
  const calls = [];
  const fetcher = async (url, options = {}) => {
    calls.push({ url, method: options.method ?? "GET" });
    return new Response(JSON.stringify([]), { status: 200, headers: { "content-type": "application/json" } });
  };
  const grocery = {
    merchant: "H-E-B", date: "2026-11-20",
    items: [{ description: "Milk", quantity: "2", unitPrice: "3.99", total: "7.98" }],
    total: "7.98"
  };
  const catalog = { products: [{ id: "7", name: "Whole Milk", qu_factor_purchase_to_stock: 1 }], barcodes: [] };
  const out = await dispatchSpecialists(fetcher,
    { enabled: true, grocy: { baseUrl: "https://grocy.test", token: "token", catalog } },
    { routes: ["tracer", "grocy"] }, grocery, source, {});
  assert.equal(out.grocy.held, true);
  assert.equal(calls.filter(c => c.method !== "GET").length, 0);
});
