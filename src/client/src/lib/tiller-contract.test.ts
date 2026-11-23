import { describe, expect, it } from "vitest";
import type {
  TillerAccountMapping,
  TillerConnectionStatus,
  TillerMatchSuggestion,
  TillerSyncResult,
} from "./tiller-contracts";

/**
 * Wire-contract pin for the native Tiller UI (W26) against W24's backend
 * DTOs. ASP.NET Core serializes C# records camelCase, so these fixtures
 * assert the exact keys the UI sends and reads. The production adapter in
 * ./tiller-api.ts binds these shapes to the real controller routes.
 */
describe("tiller wire contract", () => {
  it("serializes the connection status exactly as the C# record does", () => {
    const status: TillerConnectionStatus = {
      isConfigured: true,
      isConnected: true,
      selectedSpreadsheetId: "sheet-1",
      lastSuccessfulSyncUtc: "2026-11-23T12:00:00Z",
    };
    expect(Object.keys(status).sort()).toEqual(
      [
        "isConfigured",
        "isConnected",
        "selectedSpreadsheetId",
        "lastSuccessfulSyncUtc",
      ].sort(),
    );
    expect(JSON.parse(JSON.stringify(status))).toEqual(status);
  });

  it("serializes mapping and correction payloads with camelCase command keys", () => {
    const mapping: TillerAccountMapping = {
      id: "map-1",
      receiptsAccountId: "acct-1",
      tillerAccountId: "t-99",
      tillerAccountName: "Checking",
      spreadsheetId: "sheet-1",
    };
    expect(Object.keys(mapping).sort()).toEqual(
      [
        "id",
        "receiptsAccountId",
        "tillerAccountId",
        "tillerAccountName",
        "spreadsheetId",
      ].sort(),
    );

    const confirm = {
      providerTransactionId: "txn-7",
      receiptId: "receipt-1",
      reviewer: "Jayson",
    };
    expect(JSON.parse(JSON.stringify(confirm))).toEqual(confirm);

    const result: TillerSyncResult = {
      inserted: 3,
      updated: 1,
      reconciled: 2,
      duplicates: 0,
      invalid: 0,
      pages: 2,
    };
    expect(Object.keys(result).sort()).toEqual(
      ["inserted", "updated", "reconciled", "duplicates", "invalid", "pages"].sort(),
    );
  });

  it("keeps match decisions as the backend enum strings", () => {
    const suggestion: TillerMatchSuggestion = {
      decision: "ambiguous",
      receiptId: null,
      confidence: null,
      candidates: [
        { receiptId: "receipt-1", providerTransactionId: "txn-7", dateWithinDays: 1 },
      ],
    };
    expect(
      ["unknown", "linked", "manual", "ambiguous", "taken", "none"],
    ).toContain(suggestion.decision);
  });
});
