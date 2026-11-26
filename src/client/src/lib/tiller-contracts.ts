/**
 * Native Tiller UI contract (W26). TypeScript mirrors of W24's C# DTOs,
 * serialized camelCase per ASP.NET Core web defaults. Financial rows stay
 * machine-side: screens show counts, status, suggestions and neutral
 * unmatched context — never scores, nags or required tasks.
 *
 * Transport note: no route strings live here. The single binding seam is
 * `./tiller-api`, whose functions W20 binds to W24 slice-5 routes. Nothing
 * in this file invents an endpoint or a second transport.
 */

export interface TillerConnectionStatus {
  isConfigured: boolean;
  isConnected: boolean;
  selectedSpreadsheetId: string | null;
  lastSuccessfulSyncUtc: string | null;
  executorManaged?: boolean;
}

export interface TillerSpreadsheet {
  spreadsheetId: string;
  title: string;
  /**
   * Direct Fill: the provider backend fills the sheet. W24-validated
   * semantics: tools work only on readable sheets and a non-editable
   * sheet errors on reads too, so it can never back a sync. This is a
   * provider readability flag, never a write permission (the app never
   * writes upstream).
   */
  editable: boolean;
  backendAutoFill: boolean;
  accountCount: number;
}

export interface TillerAccount {
  accountId: string;
  displayName: string;
  institution: string;
  accountType: string;
  class: string;
}

export interface TillerCategory {
  name: string;
  group: string;
  type: string;
}

export interface TillerAccountMapping {
  id: string;
  receiptsAccountId: string;
  tillerAccountId: string;
  tillerAccountName: string;
  spreadsheetId: string;
}

export interface TillerCreateAccountMapping {
  receiptsAccountId: string;
  tillerAccountId: string;
  tillerAccountName: string;
  spreadsheetId: string;
}

export interface TillerUpdateAccountMapping {
  id: string;
  tillerAccountId: string;
  tillerAccountName: string;
  spreadsheetId: string;
}

export interface TillerSyncResult {
  inserted: number;
  updated: number;
  reconciled: number;
  duplicates: number;
  invalid: number;
  pages: number;
}

export interface TillerSelectedSpreadsheet {
  selectedSpreadsheetId: string | null;
}

export interface TillerSyncStatus {
  spreadsheetId: string | null;
  syncedThroughDate: string | null;
  completed: boolean;
  lastSuccessfulSyncUtc: string | null;
}

export interface TillerStoredSyncEvent {
  id: string;
  eventType: string;
  success: boolean;
  inserted: number;
  updated: number;
  reconciled: number;
  duplicates: number;
  errorMessage: string | null;
  occurredAt: string;
}

export interface TillerSyncEventList {
  rows: TillerStoredSyncEvent[];
  total: number;
}

export interface TillerStoredTransaction {
  id: string;
  providerTransactionId: string;
  date: string;
  description: string;
  amount: number;
  category: string | null;
  account: string;
  institution: string;
  status: string;
  splitGroupId: string | null;
  revision: number;
  linkKind: string | null;
  linkedReceiptId: string | null;
}

export type TillerMatchDecision =
  | "unknown"
  | "linked"
  | "manual"
  | "ambiguous"
  | "taken"
  | "none"
  | "possibletip";

export interface TillerMatchCandidate {
  receiptId: string;
  providerTransactionId: string;
  dateWithinDays: number;
}

export interface TillerMatchSuggestion {
  decision: TillerMatchDecision;
  receiptId: string | null;
  confidence: string | null;
  candidates: TillerMatchCandidate[];
  suggestedTip?: number | null;
}

export interface TillerLinkHistoryEntry {
  action: string;
  receiptId: string | null;
  reviewer: string | null;
  at: string;
}

export interface TillerPaymentContext {
  providerTransactionId: string;
  date: string;
  description: string;
  amount: number;
  account: string;
  category: string | null;
  linkKind: string | null;
}
