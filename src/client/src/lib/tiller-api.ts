/**
 * Production adapter for the native Tiller UI (W26) against W24's actual
 * controller (`API.Controllers.TillerController`, base `api/tiller`).
 * Uses the app's existing authenticated openapi-fetch client; the local
 * `TillerPaths` type mirrors the controller's routes and DTOs 1:1 until
 * W20 folds this surface into the generated spec (spec fragment + client
 * regeneration stay W20's hunks — no shared files touched here).
 *
 * Conventions honored: 503/400 surface the server's RFC 9457 detail;
 * `suggestion`/`payment-context` 404s become neutral local values
 * (`unknown` decision / null) instead of errors; list wrappers
 * (`{data:[...]}`) are unwrapped here so screens stay simple. No
 * endpoint invented, no transport added, no provider writes possible:
 * every route below exists in the controller source.
 */
import client from "@/lib/api-client";
import type { Client } from "openapi-fetch";
import { localErrorPolicy } from "@/lib/request-error-policy";
import {
  type TillerAccount,
  type TillerAccountMapping,
  type TillerConnectionStatus,
  type TillerCreateAccountMapping,
  type TillerLinkHistoryEntry,
  type TillerMatchSuggestion,
  type TillerPaymentContext,
  type TillerSelectedSpreadsheet,
  type TillerSpreadsheet,
  type TillerSyncEventList,
  type TillerSyncResult,
  type TillerSyncStatus,
  type TillerStoredTransaction,
  type TillerUpdateAccountMapping,
} from "./tiller-contracts";

type Json<T> = { "application/json": T };
type NoParams = {
  query?: never;
  header?: never;
  path?: never;
  cookie?: never;
};
type Headers = { [name: string]: unknown };
type Ok<T> = { headers: Headers; content: Json<T> };
type NoContent = { headers: Headers; content?: never };
type ProblemJson = {
  "application/problem+json": { detail?: string; title?: string; status?: number };
};
type BadRequest = { headers: Headers; content: ProblemJson };
type ServiceUnavailable = { headers: Headers; content: ProblemJson };
type NotFoundEmpty = { headers: Headers; content?: never };

export interface TillerTransactionPage {
  rows: TillerStoredTransaction[];
  total: number;
}

interface TillerPaths {
  "/api/tiller/transactions": {
    get: {
      parameters: {
        query?: { offset?: number; limit?: number; sortBy?: string; sortDirection?: string; unlinkedOnly?: boolean };
      };
      responses: { 200: Ok<TillerTransactionPage>; 400: BadRequest };
    };
  };
  "/api/tiller/connection-status": {
    get: {
      parameters: NoParams;
      responses: { 200: Ok<TillerConnectionStatus> };
    };
  };
  "/api/tiller/status": {
    get: {
      parameters: NoParams;
      responses: { 200: Ok<TillerSyncStatus> };
    };
  };
  "/api/tiller/spreadsheets": {
    get: {
      parameters: NoParams;
      responses: { 200: Ok<TillerSpreadsheet[]> };
    };
  };
  "/api/tiller/settings/spreadsheet": {
    get: {
      parameters: NoParams;
      responses: { 200: Ok<TillerSelectedSpreadsheet> };
    };
    put: {
      parameters: NoParams;
      requestBody: { content: Json<{ spreadsheetId: string }> };
      responses: { 200: Ok<TillerSelectedSpreadsheet>; 400: BadRequest; 503: ServiceUnavailable };
    };
  };
  "/api/tiller/accounts": {
    get: {
      parameters: NoParams;
      responses: { 200: Ok<TillerAccount[]> };
    };
  };
  "/api/tiller/account-mappings": {
    get: {
      parameters: NoParams;
      responses: { 200: Ok<{ data: TillerAccountMapping[] }> };
    };
    post: {
      parameters: NoParams;
      requestBody: { content: Json<TillerCreateAccountMapping> };
      responses: { 200: Ok<TillerAccountMapping> };
    };
  };
  "/api/tiller/account-mappings/{id}": {
    put: {
      parameters: { path: { id: string } };
      requestBody: {
        content: Json<{ tillerAccountId: string; tillerAccountName: string; spreadsheetId: string }>;
      };
      responses: { 204: NoContent; 400: BadRequest };
    };
    delete: {
      parameters: { path: { id: string } };
      responses: { 204: NoContent; 404: NotFoundEmpty };
    };
  };
  "/api/tiller/sync": {
    post: {
      parameters: NoParams;
      requestBody: { content: Json<{ mode: string }> };
      responses: { 200: Ok<TillerSyncResult>; 400: BadRequest; 503: ServiceUnavailable };
    };
  };
  "/api/tiller/events": {
    get: {
      parameters: {
        query?: { offset?: number; limit?: number; outcome?: string };
      };
      responses: { 200: Ok<TillerSyncEventList> };
    };
  };
  "/api/tiller/transactions/{providerTransactionId}/suggestion": {
    get: {
      parameters: { path: { providerTransactionId: string } };
      responses: { 200: Ok<TillerMatchSuggestion> };
    };
  };
  "/api/tiller/transactions/{providerTransactionId}/history": {
    get: {
      parameters: { path: { providerTransactionId: string } };
      responses: { 200: Ok<{ data: TillerLinkHistoryEntry[] }> };
    };
  };
  "/api/tiller/transactions/{providerTransactionId}/confirm": {
    post: {
      parameters: { path: { providerTransactionId: string } };
      requestBody: { content: Json<{ receiptId: string; reviewer?: string | null }> };
      responses: { 200: Ok<TillerMatchSuggestion>; 400: BadRequest; 404: NotFoundEmpty };
    };
  };
  "/api/tiller/transactions/{providerTransactionId}/unlink": {
    post: {
      parameters: { path: { providerTransactionId: string } };
      requestBody?: { content: Json<{ reviewer?: string | null }> };
      responses: { 200: Ok<TillerMatchSuggestion>; 404: NotFoundEmpty };
    };
  };
  "/api/tiller/receipts/{receiptId}/payment-context": {
    get: {
      parameters: { path: { receiptId: string } };
      responses: { 200: Ok<TillerPaymentContext> };
    };
  };
}

const tiller = client as unknown as Client<TillerPaths>;

function problemDetail(error: unknown): string | undefined {
  if (typeof error !== "object" || error === null) return undefined;
  const detail = (error as { detail?: unknown }).detail;
  return typeof detail === "string" && detail.length > 0 ? detail : undefined;
}

async function read<T>(
  label: string,
  result: { data?: T; error?: unknown; response: Response },
  notFound?: () => T,
): Promise<T> {
  if (result.response.status === 404 && notFound) return notFound();
  if (result.error !== undefined || result.data === undefined) {
    throw new Error(
      problemDetail(result.error) ?? `${label} failed (HTTP ${result.response.status})`,
    );
  }
  return result.data;
}

function unknownSuggestion(): TillerMatchSuggestion {
  return { decision: "unknown", receiptId: null, confidence: null, candidates: [] };
}

export function getTillerConnectionStatus(signal?: AbortSignal) {
  return tiller
    .GET("/api/tiller/connection-status", { signal })
    .then((r) => read("Tiller connection status", r));
}

export function getTillerSyncStatus(signal?: AbortSignal) {
  return tiller.GET("/api/tiller/status", { signal }).then((r) => read("Tiller sync status", r));
}

export function listTillerSpreadsheets(signal?: AbortSignal) {
  return tiller
    .GET("/api/tiller/spreadsheets", { signal })
    .then((r) => read("Tiller spreadsheets", r));
}

export function getSelectedTillerSpreadsheet(signal?: AbortSignal) {
  return tiller
    .GET("/api/tiller/settings/spreadsheet", { signal })
    .then((r) => read("Selected Tiller spreadsheet", r));
}

export function selectTillerSpreadsheet(spreadsheetId: string) {
  return tiller
    .PUT("/api/tiller/settings/spreadsheet", { body: { spreadsheetId } })
    .then((r) => read("Select Tiller spreadsheet", r));
}

export function listTillerAccounts(signal?: AbortSignal) {
  return tiller.GET("/api/tiller/accounts", { signal }).then((r) => read("Tiller accounts", r));
}

export function listTillerAccountMappings(signal?: AbortSignal) {
  return tiller
    .GET("/api/tiller/account-mappings", { signal })
    .then((r) => read("Tiller account mappings", r).then((list) => list.data));
}

export function createTillerAccountMapping(mapping: TillerCreateAccountMapping) {
  return tiller
    .POST("/api/tiller/account-mappings", { body: mapping })
    .then((r) => read("Create Tiller account mapping", r));
}

export async function updateTillerAccountMapping(mapping: TillerUpdateAccountMapping) {
  const { id, ...body } = mapping;
  const r = await tiller.PUT("/api/tiller/account-mappings/{id}", {
    params: { path: { id } },
    body,
  });
  if (r.error !== undefined) {
    throw new Error(
      problemDetail(r.error) ?? `Update Tiller account mapping failed (HTTP ${r.response.status})`,
    );
  }
}

export async function deleteTillerAccountMapping(id: string) {
  const r = await tiller.DELETE("/api/tiller/account-mappings/{id}", {
    params: { path: { id } },
  });
  if (r.error !== undefined) {
    throw new Error(
      problemDetail(r.error) ?? `Delete Tiller account mapping failed (HTTP ${r.response.status})`,
    );
  }
}

export function syncTiller(mode: "full" | "incremental") {
  return tiller.POST("/api/tiller/sync", { body: { mode } }).then((r) => read("Tiller sync", r));
}

export function listTillerSyncEvents(
  offset: number,
  limit: number,
  outcome?: "success" | "failure",
  signal?: AbortSignal,
) {
  return tiller
    .GET("/api/tiller/events", {
      params: { query: { offset, limit, outcome } },
      signal,
    })
    .then((r) => read("Tiller sync events", r));
}

export function getTillerMatchSuggestion(providerTransactionId: string, signal?: AbortSignal) {
  return tiller
    .GET("/api/tiller/transactions/{providerTransactionId}/suggestion", {
      params: { path: { providerTransactionId } },
      signal,
    })
    .then((r) => read("Tiller match suggestion", r, unknownSuggestion));
}

export function getTillerLinkHistory(providerTransactionId: string, signal?: AbortSignal) {
  return tiller
    .GET("/api/tiller/transactions/{providerTransactionId}/history", {
      params: { path: { providerTransactionId } },
      signal,
    })
    .then((r) => read("Tiller link history", r).then((list) => list.data));
}

export function confirmTillerLink(args: {
  providerTransactionId: string;
  receiptId: string;
  reviewer?: string | null;
}) {
  return tiller
    .POST("/api/tiller/transactions/{providerTransactionId}/confirm", {
      params: { path: { providerTransactionId: args.providerTransactionId } },
      body: { receiptId: args.receiptId, reviewer: args.reviewer ?? null },
    })
    .then((r) => read("Confirm Tiller link", r));
}

export function unlinkTillerLink(args: { providerTransactionId: string; reviewer?: string | null }) {
  const body = args.reviewer ? { reviewer: args.reviewer } : {};
  return tiller
    .POST("/api/tiller/transactions/{providerTransactionId}/unlink", {
      params: { path: { providerTransactionId: args.providerTransactionId } },
      body,
    })
    .then((r) => read("Unlink Tiller link", r, unknownSuggestion));
}

export function getTillerPaymentContext(receiptId: string, signal?: AbortSignal) {
  return tiller
    .GET("/api/tiller/receipts/{receiptId}/payment-context", {
      params: { path: { receiptId } },
      signal,
    })
    .then((r) => read("Tiller payment context", r, () => null));
}

/** Read a bounded page of the local cache; never fetch or write the provider. */
export function listCachedTillerTransactions(
  offset: number,
  limit: number,
  unlinkedOnly: boolean,
  signal?: AbortSignal,
) {
  return tiller.GET("/api/tiller/transactions", {
    params: { query: { offset, limit, sortBy: "date", sortDirection: "desc", unlinkedOnly } },
    signal,
    ...localErrorPolicy.request,
  }).then((r) => read("Cached Tiller transactions", r));
}
