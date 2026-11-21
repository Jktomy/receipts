// Tracer specialist dispatch: runs only after human approval, only when the
// staged opt-in is enabled, and only for routes on the approved draft.
// Grocy stock mutation is never performed here: the grocy branch always
// returns a held proposal (separate inventory approval required).
// MyGarage writes are idempotent via the source tag and never invent
// vehicles, quantities, or mileage.
import { resolveVehicle, toFuelRecord, postFuelRecord, toServiceVisit, postServiceVisit } from "./specialist-mygarage.mjs";
import { dispatchGrocyPurchase } from "./specialist-grocy.mjs";

function hasFuelItem(qualified) {
  const items = Array.isArray(qualified?.items) ? qualified.items : [];
  return items.some(i => {
    const u = (i?.unitOfMeasure ?? i?.unit ?? "").toString().trim().toLowerCase();
    return (u === "gal" || u === "gallon" || u === "gallons" || u === "l" || u === "liter" || u === "liters" || u === "litre" || u === "litres") && Number(i?.quantity) > 0;
  });
}

export async function dispatchSpecialists(fetcher, config, draft, qualified, source, approval = {}) {
  const routes = Array.isArray(draft?.routes) ? draft.routes : [];
  if (config?.enabled !== true) return { dispatched: false, reason: "specialist dispatch is disabled (staged only)" };
  const out = { dispatched: false };
  if (routes.includes("mygarage") && config?.mygarage) {
    const mg = config.mygarage;
    const vin = approval.selectedVehicleVin;
    if (!vin) {
      out.mygarage = { held: true, reason: "Reviewer must select exactly one vehicle" };
    } else {
      const configured = Array.isArray(mg.vehicles) ? mg.vehicles : [];
      const vehicle = configured.length > 0
        ? resolveVehicle(configured, vin)
        : { vin: String(vin) };
      if (hasFuelItem(qualified)) {
        const record = toFuelRecord(qualified, vehicle, source);
        out.fuel = await postFuelRecord(fetcher, mg.baseUrl, mg.token, vehicle.vin, record, source);
        out.dispatched = true;
      } else if (approval.serviceCategory) {
        const { visit, lineItems } = toServiceVisit(qualified, vehicle, source, { category: approval.serviceCategory });
        out.service = await postServiceVisit(fetcher, mg.baseUrl, mg.token, vehicle.vin, visit, lineItems, source);
        out.dispatched = true;
      } else {
        out.mygarage = { held: true, reason: "Neither a fuel quantity nor a reviewer-selected service category is present" };
      }
    }
  }
  if (routes.includes("grocy") && config?.grocy) {
    const g = config.grocy;
    out.grocy = await dispatchGrocyPurchase(fetcher, g.baseUrl, g.token, qualified, source,
      { products: g.catalog?.products ?? [], barcodes: g.catalog?.barcodes ?? [] }, {});
  }
  if (!out.dispatched && !out.grocy) out.reason = "no specialist route applied";
  return out;
}
