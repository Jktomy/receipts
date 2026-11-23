vi.hoisted(() => vi.stubEnv("VITE_API_URL", "http://tiller-adapter.test"));

import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { setTokens, clearTokens } from "@/lib/auth";
import {
  confirmTillerLink,
  createTillerAccountMapping,
  deleteTillerAccountMapping,
  getSelectedTillerSpreadsheet,
  getTillerConnectionStatus,
  getTillerLinkHistory,
  getTillerMatchSuggestion,
  getTillerPaymentContext,
  getTillerSyncStatus,
  listTillerAccountMappings,
  listTillerAccounts,
  listTillerSpreadsheets,
  listTillerSyncEvents,
  selectTillerSpreadsheet,
  syncTiller,
  unlinkTillerLink,
  updateTillerAccountMapping,
} from "./tiller-api";

interface Seen {
  method: string;
  url: string;
  body: unknown;
  auth: string | null;
}

const seen: Seen[] = [];

function capture(request: Request) {
  return (async () => {
    let body: unknown = null;
    try {
      body = await request.clone().json();
    } catch {
      body = null;
    }
    seen.push({
      method: request.method,
      url: request.url,
      body,
      auth: request.headers.get("authorization"),
    });
  })();
}

const server = setupServer(
  http.get("*/api/tiller/connection-status", async ({ request }) => {
    await capture(request);
    return HttpResponse.json({
      isConfigured: true,
      isConnected: true,
      selectedSpreadsheetId: "sheet-1",
      lastSuccessfulSyncUtc: "2026-11-23T12:00:00Z",
    });
  }),
  http.get("*/api/tiller/status", async ({ request }) => {
    await capture(request);
    return HttpResponse.json({
      spreadsheetId: "sheet-1",
      syncedThroughDate: "2026-11-22",
      completed: true,
      lastSuccessfulSyncUtc: null,
    });
  }),
  http.get("*/api/tiller/spreadsheets", async ({ request }) => {
    await capture(request);
    return HttpResponse.json([
      {
        spreadsheetId: "sheet-1",
        title: "Budget 2026",
        editable: true,
        backendAutoFill: true,
        accountCount: 2,
      },
    ]);
  }),
  http.get("*/api/tiller/settings/spreadsheet", async ({ request }) => {
    await capture(request);
    return HttpResponse.json({ selectedSpreadsheetId: "sheet-1" });
  }),
  http.put("*/api/tiller/settings/spreadsheet", async ({ request }) => {
    await capture(request);
    return HttpResponse.json({ selectedSpreadsheetId: "sheet-2" });
  }),
  http.get("*/api/tiller/accounts", async ({ request }) => {
    await capture(request);
    return HttpResponse.json([
      {
        accountId: "t-99",
        displayName: "Checking",
        institution: "Bank",
        accountType: "checking",
        class: "asset",
      },
    ]);
  }),
  http.get("*/api/tiller/account-mappings", async ({ request }) => {
    await capture(request);
    return HttpResponse.json({
      data: [
        {
          id: "map-1",
          receiptsAccountId: "acct-1",
          tillerAccountId: "t-99",
          tillerAccountName: "Checking",
          spreadsheetId: "sheet-1",
        },
      ],
    });
  }),
  http.post("*/api/tiller/account-mappings", async ({ request }) => {
    await capture(request);
    return HttpResponse.json({
      id: "map-2",
      receiptsAccountId: "acct-1",
      tillerAccountId: "t-99",
      tillerAccountName: "Checking",
      spreadsheetId: "sheet-1",
    });
  }),
  http.put("*/api/tiller/account-mappings/:id", async ({ request }) => {
    await capture(request);
    return new HttpResponse(null, { status: 204 });
  }),
  http.delete("*/api/tiller/account-mappings/:id", async ({ request }) => {
    await capture(request);
    return new HttpResponse(null, { status: 204 });
  }),
  http.post("*/api/tiller/sync", async ({ request }) => {
    await capture(request);
    return HttpResponse.json({
      inserted: 3,
      updated: 1,
      reconciled: 2,
      duplicates: 0,
      invalid: 0,
      pages: 2,
    });
  }),
  http.get("*/api/tiller/events", async ({ request }) => {
    await capture(request);
    return HttpResponse.json({
      rows: [
        {
          id: "ev-1",
          eventType: "Full",
          success: true,
          inserted: 3,
          updated: 1,
          reconciled: 2,
          duplicates: 0,
          errorMessage: null,
          occurredAt: "2026-11-23T12:00:00Z",
        },
      ],
      total: 1,
    });
  }),
  http.get("*/api/tiller/transactions/:id/suggestion", async ({ request, params }) => {
    await capture(request);
    if (params.id === "missing") return new HttpResponse(null, { status: 404 });
    return HttpResponse.json({
      decision: "ambiguous",
      receiptId: null,
      confidence: null,
      candidates: [{ receiptId: "receipt-1", providerTransactionId: "txn-7", dateWithinDays: 1 }],
    });
  }),
  http.get("*/api/tiller/transactions/:id/history", async ({ request }) => {
    await capture(request);
    return HttpResponse.json({
      data: [{ action: "confirm", receiptId: "receipt-1", reviewer: "anna", at: "2026-11-22T10:00:00Z" }],
    });
  }),
  http.post("*/api/tiller/transactions/:id/confirm", async ({ request }) => {
    await capture(request);
    return HttpResponse.json({
      decision: "manual",
      receiptId: "receipt-1",
      confidence: null,
      candidates: [],
    });
  }),
  http.post("*/api/tiller/transactions/:id/unlink", async ({ request }) => {
    await capture(request);
    return HttpResponse.json({
      decision: "none",
      receiptId: null,
      confidence: null,
      candidates: [],
    });
  }),
  http.get("*/api/tiller/receipts/:id/payment-context", async ({ request, params }) => {
    await capture(request);
    if (params.id === "00000000-0000-4000-8000-000000000000")
      return new HttpResponse(null, { status: 404 });
    return HttpResponse.json({
      providerTransactionId: "txn-7",
      date: "2026-11-20",
      description: "WHOLEFDS MKT",
      amount: 42.18,
      account: "Checking",
      category: "Groceries",
      linkKind: "manual",
    });
  }),
  http.get("*/api/tiller/failing", async () => {
    return HttpResponse.json(
      { detail: "Tiller is not configured" },
      { status: 503, headers: { "content-type": "application/problem+json" } },
    );
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterAll(() => server.close());
afterEach(() => {
  clearTokens();
  server.resetHandlers();
  seen.length = 0;
});

describe("tiller production adapter", () => {
  it("sends the bearer token and parses connection status", async () => {
    setTokens("access", "refresh");
    const status = await getTillerConnectionStatus();
    expect(status).toMatchObject({ isConfigured: true, selectedSpreadsheetId: "sheet-1" });
    expect(seen).toHaveLength(1);
    expect(seen[0].auth).toBe("Bearer access");
  });

  it("reads sync status and spreadsheets with backend fields", async () => {
    const status = await getTillerSyncStatus();
    expect(status.completed).toBe(true);
    const sheets = await listTillerSpreadsheets();
    expect(sheets[0]).toMatchObject({ backendAutoFill: true, accountCount: 2 });
  });

  it("selects a spreadsheet with PUT and reads the selected object", async () => {
    const selected = await getSelectedTillerSpreadsheet();
    expect(selected).toEqual({ selectedSpreadsheetId: "sheet-1" });
    const next = await selectTillerSpreadsheet("sheet-2");
    expect(next).toEqual({ selectedSpreadsheetId: "sheet-2" });
    const put = seen.find((s) => s.method === "PUT");
    expect(put?.url).toContain("/api/tiller/settings/spreadsheet");
    expect(put?.body).toEqual({ spreadsheetId: "sheet-2" });
  });

  it("round-trips account mappings including 204 updates and deletes", async () => {
    const accounts = await listTillerAccounts();
    expect(accounts[0]).toMatchObject({ displayName: "Checking", accountType: "checking" });
    const mappings = await listTillerAccountMappings();
    expect(mappings).toHaveLength(1);
    const created = await createTillerAccountMapping({
      receiptsAccountId: "acct-1",
      tillerAccountId: "t-99",
      tillerAccountName: "Checking",
      spreadsheetId: "sheet-1",
    });
    expect(created.id).toBe("map-2");
    const post = seen.find((s) => s.method === "POST" && s.url.endsWith("/api/tiller/account-mappings"));
    expect(post?.body).toMatchObject({ tillerAccountId: "t-99", spreadsheetId: "sheet-1" });
    await updateTillerAccountMapping({
      id: "map-1",
      tillerAccountId: "t-99",
      tillerAccountName: "Checking",
      spreadsheetId: "sheet-1",
    });
    await deleteTillerAccountMapping("map-1");
    expect(seen.some((s) => s.method === "PUT" && s.url.endsWith("/api/tiller/account-mappings/map-1"))).toBe(true);
    expect(seen.some((s) => s.method === "DELETE" && s.url.endsWith("/api/tiller/account-mappings/map-1"))).toBe(true);
  });

  it("syncs by mode and pages events with the outcome filter", async () => {
    const result = await syncTiller("incremental");
    expect(result).toMatchObject({ inserted: 3, pages: 2 });
    const events = await listTillerSyncEvents(0, 10, "success");
    expect(events.total).toBe(1);
    const get = seen.find((s) => s.url.includes("/api/tiller/events"));
    expect(get?.url).toContain("outcome=success");
  });

  it("confirms and unlinks with serialized correction payloads", async () => {
    const suggestion = await getTillerMatchSuggestion("txn-7");
    expect(suggestion.decision).toBe("ambiguous");
    const missing = await getTillerMatchSuggestion("missing");
    expect(missing).toMatchObject({ decision: "unknown", candidates: [] });
    const confirmed = await confirmTillerLink({
      providerTransactionId: "txn-7",
      receiptId: "receipt-1",
      reviewer: "anna",
    });
    expect(confirmed.decision).toBe("manual");
    const confirmCall = seen.find((s) => s.url.endsWith("/txn-7/confirm"));
    expect(confirmCall?.body).toEqual({ receiptId: "receipt-1", reviewer: "anna" });
    const history = await getTillerLinkHistory("txn-7");
    expect(history[0]).toMatchObject({ action: "confirm", reviewer: "anna" });
    const unlinked = await unlinkTillerLink({ providerTransactionId: "txn-7" });
    expect(unlinked.decision).toBe("none");
    const unlinkCall = seen.find((s) => s.url.endsWith("/txn-7/unlink"));
    expect(unlinkCall?.body).toEqual({});
  });

  it("reads payment context and maps unknown receipts to null", async () => {
    const context = await getTillerPaymentContext("receipt-1");
    expect(context).toMatchObject({ providerTransactionId: "txn-7", amount: 42.18 });
    const missing = await getTillerPaymentContext("00000000-0000-4000-8000-000000000000");
    expect(missing).toBeNull();
  });

  it("surfaces the server problem detail on failures", async () => {
    server.use(
      http.get("*/api/tiller/spreadsheets", () =>
        HttpResponse.json(
          { detail: "provider timeout" },
          { status: 503, headers: { "content-type": "application/problem+json" } },
        ),
      ),
    );
    await expect(listTillerSpreadsheets()).rejects.toThrow("provider timeout");
  });
});
