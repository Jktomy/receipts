// Tracer specialist dispatch: MyGarage fuel and service records.
// All writes happen only after human approval, gated by an explicit opt-in
// plus routes containing "mygarage". Auth scheme (Bearer) is verified at
// activation against the installed MyGarage; a 401 throws loudly, never
// silently succeeds.

const GAL_TO_L = 3.78541;
const MI_TO_KM = 1.60934;

export function resolveVehicle(vehicles, hint) {
  const list = Array.isArray(vehicles) ? vehicles : [];
  const q = (hint ?? "").toString().trim().toLowerCase();
  const matches = q
    ? list.filter(v => (v.vin ?? "").toLowerCase() === q || (v.label ?? "").toLowerCase().includes(q))
    : [...list];
  if (matches.length === 0) throw new Error("No vehicle matches this receipt: reviewer must select a vehicle, never invent one");
  if (matches.length > 1) throw new Error("Ambiguous vehicle: reviewer must select exactly one vehicle");
  return matches[0];
}

function fuelUnit(item) {
  return (item?.unitOfMeasure ?? item?.unit ?? "").toString().trim().toLowerCase();
}

function fuelLiters(item) {
  const qty = Number(item?.quantity);
  if (!Number.isFinite(qty) || qty <= 0) throw new Error("Fuel item has no usable quantity");
  const unit = fuelUnit(item);
  if (unit === "gal" || unit === "gallon" || unit === "gallons") return qty * GAL_TO_L;
  if (unit === "l" || unit === "liter" || unit === "liters" || unit === "litre" || unit === "litres") return qty;
  throw new Error("Fuel quantity needs gallons or liters, refusing to guess");
}

function odometerKm(vehicle) {
  if (vehicle?.odometer == null || vehicle.odometer === "") return undefined;
  const value = Number(vehicle.odometer);
  if (!Number.isFinite(value) || value < 0) throw new Error("Odometer reading is not a number");
  const unit = (vehicle.odometerUnit ?? "mi").toString().trim().toLowerCase();
  if (unit === "mi" || unit === "mile" || unit === "miles") return value * MI_TO_KM;
  if (unit === "km" || unit === "kilometer" || unit === "kilometers") return value;
  throw new Error("Odometer unit must be miles or kilometers");
}

export function sourceTag(source) {
  return `tracer:paperless:${source.paperlessId}:${String(source.contentHash).slice(0, 8)}`;
}

function fuelItem(qualified) {
  const items = Array.isArray(qualified?.items) ? qualified.items : [];
  for (const item of items) {
    try {
      return { item, liters: fuelLiters(item) };
    } catch {
      continue;
    }
  }
  throw new Error("No fuel quantity on this receipt: refusing to post a guess");
}

export function toFuelRecord(qualified, vehicle, source) {
  if (!vehicle?.vin) throw new Error("A resolved vehicle VIN is required");
  const { liters } = fuelItem(qualified);
  const roundedLiters = Math.round(liters * 1000) / 1000;
  const record = {
    vin: vehicle.vin,
    date: qualified.date,
    liters: roundedLiters,
    station_name_freetext: qualified.merchant ?? null,
    notes: `${sourceTag(source)} ${qualified.merchant ?? ""} ${qualified.date ?? ""}`.trim()
  };
  const cost = Number(qualified?.total);
  if (Number.isFinite(cost) && cost > 0) {
    record.cost = Math.round(cost * 100) / 100;
    record.price_per_unit = Math.round((cost / roundedLiters) * 1000) / 1000;
  }
  const km = odometerKm(vehicle);
  if (km !== undefined) record.odometer_km = Math.round(km * 100) / 100;
  return record;
}

const SERVICE_CATEGORIES = ["Maintenance", "Inspection", "Collision", "Upgrades", "Detailing"];

export function toServiceVisit(qualified, vehicle, source, options = {}) {
  if (!vehicle?.vin) throw new Error("A resolved vehicle VIN is required");
  const category = options.category;
  if (!SERVICE_CATEGORIES.includes(category)) {
    throw new Error(`Service category must be one of ${SERVICE_CATEGORIES.join(", ")}: reviewer selects, never inferred`);
  }
  const visit = {
    date: qualified.date,
    service_category: category,
    notes: `${sourceTag(source)} ${qualified.merchant ?? ""} ${qualified.date ?? ""} Paperless doc ${source.paperlessId}`.trim()
  };
  const km = odometerKm(vehicle);
  if (km !== undefined) visit.odometer_km = Math.round(km * 100) / 100;
  const lineItems = (Array.isArray(qualified?.items) ? qualified.items : [])
    .filter(i => i?.description && Number(i?.total) > 0)
    .map(i => ({
      description: String(i.description).slice(0, 200),
      category,
      cost: Math.round(Number(i.total) * 100) / 100
    }));
  return { visit, lineItems };
}

export async function postServiceVisit(fetcher, baseUrl, token, vin, visit, lineItems, source) {
  const tag = sourceTag(source);
  const visitsUrl = `${baseUrl}/api/vehicles/${encodeURIComponent(vin)}/service-visits`;
  const existing = await readJson(await fetcher(visitsUrl, {
    method: "GET", headers: { Authorization: `Bearer ${token}`, Accept: "application/json" }
  }), "MyGarage");
  const rows = Array.isArray(existing) ? existing : (existing?.data ?? existing?.records ?? []);
  if (rows.some(r => typeof r?.notes === "string" && r.notes.includes(tag))) {
    return { deduplicated: true };
  }
  const created = await readJson(await fetcher(visitsUrl, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}`, Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify(visit)
  }), "MyGarage");
  const visitId = created?.id;
  if (visitId == null) throw new Error("MyGarage did not return a service visit id");
  for (const item of lineItems) {
    await readJson(await fetcher(`${visitsUrl}/${encodeURIComponent(String(visitId))}/line-items`, {
      method: "POST",
      headers: { Authorization: `Bearer ${token}`, Accept: "application/json", "Content-Type": "application/json" },
      body: JSON.stringify(item)
    }), "MyGarage");
  }
  return { deduplicated: false, id: visitId };
}

async function readJson(response, service) {
  if (!response.ok) throw new Error(`${service} returned HTTP ${response.status}`);
  return response.json();
}

export async function postFuelRecord(fetcher, baseUrl, token, vin, record, source) {
  const tag = sourceTag(source);
  const listUrl = `${baseUrl}/api/vehicles/${encodeURIComponent(vin)}/fuel`;
  const existing = await readJson(await fetcher(listUrl, {
    method: "GET", headers: { Authorization: `Bearer ${token}`, Accept: "application/json" }
  }), "MyGarage");
  const rows = Array.isArray(existing) ? existing : (existing?.data ?? existing?.records ?? []);
  if (rows.some(r => typeof r?.notes === "string" && r.notes.includes(tag))) {
    return { deduplicated: true };
  }
  const created = await readJson(await fetcher(listUrl, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}`, Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify({ ...record, vin })
  }), "MyGarage");
  return { deduplicated: false, id: created?.id ?? null };
}
