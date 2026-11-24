import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithQueryClient } from "@/test/test-utils";
import { mockMutationResult, mockQueryResult } from "@/test/mock-hooks";
import TillerSettings from "./TillerSettings";

vi.mock("@/hooks/useTillerConnection", () => ({ useTillerConnection: vi.fn() }));
vi.mock("@/hooks/useTillerSpreadsheets", () => ({
  useTillerSpreadsheets: vi.fn(),
  useSelectedTillerSpreadsheet: vi.fn(),
  useSelectTillerSpreadsheet: vi.fn(),
}));
vi.mock("@/hooks/useTillerAccountMappings", () => ({
  useTillerAccountMappings: vi.fn(),
  useTillerAccounts: vi.fn(),
  useCreateTillerAccountMapping: vi.fn(),
  useUpdateTillerAccountMapping: vi.fn(),
  useDeleteTillerAccountMapping: vi.fn(),
}));
vi.mock("@/hooks/useTillerSync", () => ({
  useTillerSyncStatus: vi.fn(),
  useTillerSyncEvents: vi.fn(),
  useSyncTiller: vi.fn(),
}));
vi.mock("@/hooks/useAccounts", () => ({ useAllAccounts: vi.fn() }));

describe("TillerSettings", () => {
  it("assembles the Tiller sections with neutral language", async () => {
    const connection = await import("@/hooks/useTillerConnection");
    vi.mocked(connection.useTillerConnection).mockReturnValue(
      mockQueryResult({ data: null, isError: true, isPending: false }),
    );
    const sheets = await import("@/hooks/useTillerSpreadsheets");
    vi.mocked(sheets.useTillerSpreadsheets).mockReturnValue(
      mockQueryResult({ data: [], isSuccess: true, isPending: false, isLoading: false }),
    );
    vi.mocked(sheets.useSelectedTillerSpreadsheet).mockReturnValue(
      mockQueryResult({ data: null, isSuccess: true, isPending: false, isLoading: false }),
    );
    vi.mocked(sheets.useSelectTillerSpreadsheet).mockReturnValue(
      mockMutationResult({}),
    );
    const mappings = await import("@/hooks/useTillerAccountMappings");
    vi.mocked(mappings.useTillerAccountMappings).mockReturnValue(
      mockQueryResult({ data: [], isSuccess: true, isPending: false, isLoading: false }),
    );
    vi.mocked(mappings.useTillerAccounts).mockReturnValue(
      mockQueryResult({ data: [], isSuccess: true, isPending: false, isLoading: false }),
    );
    vi.mocked(mappings.useCreateTillerAccountMapping).mockReturnValue(
      mockMutationResult({}),
    );
    vi.mocked(mappings.useDeleteTillerAccountMapping).mockReturnValue(
      mockMutationResult({}),
    );
    const accounts = await import("@/hooks/useAccounts");
    vi.mocked(accounts.useAllAccounts).mockReturnValue(
      mockQueryResult({ data: [], isSuccess: true, isPending: false, isLoading: false }),
    );
    const sync = await import("@/hooks/useTillerSync");
    vi.mocked(sync.useTillerSyncStatus).mockReturnValue(
      mockQueryResult({
        data: {
          spreadsheetId: null,
          syncedThroughDate: null,
          completed: false,
          lastSuccessfulSyncUtc: null,
        },
        isSuccess: true,
        isPending: false,
        isLoading: false,
      }),
    );
    vi.mocked(sync.useTillerSyncEvents).mockReturnValue(
      mockQueryResult({
        data: { rows: [], total: 0 },
        isSuccess: true,
        isPending: false,
        isLoading: false,
      }),
    );
    vi.mocked(sync.useSyncTiller).mockReturnValue(mockMutationResult({}));

    renderWithQueryClient(<TillerSettings />);
    expect(screen.getByText("Tiller Settings")).toBeInTheDocument();
    expect(screen.getByText("Tiller connection")).toBeInTheDocument();
    expect(screen.getByText("Tiller spreadsheet")).toBeInTheDocument();
    expect(screen.getByText("Tiller account mapping")).toBeInTheDocument();
    expect(screen.getByText("Tiller sync")).toBeInTheDocument();
    expect(
      screen.queryByText(/must link|action required|overdue|compliance score|needs attention/i),
    ).toBeNull();
  });
});
