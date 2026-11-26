import { describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithQueryClient } from "@/test/test-utils";
import { mockMutationResult, mockQueryResult } from "@/test/mock-hooks";
import { TillerSyncCard } from "./TillerSyncCard";
import {
  useSyncTiller,
  useTillerSyncEvents,
  useTillerSyncStatus,
} from "@/hooks/useTillerSync";

vi.mock("@/hooks/useTillerSync", () => ({
  useTillerSyncStatus: vi.fn(),
  useTillerSyncEvents: vi.fn(),
  useSyncTiller: vi.fn(),
}));

function mockStatus() {
  vi.mocked(useTillerSyncStatus).mockReturnValue(
    mockQueryResult({
      data: {
        spreadsheetId: "sheet-1",
        syncedThroughDate: "2026-11-22",
        completed: true,
        lastSuccessfulSyncUtc: "2026-11-23T12:00:00Z",
      },
      isSuccess: true,
      isPending: false,
      isLoading: false,
    }),
  );
  vi.mocked(useTillerSyncEvents).mockReturnValue(
    mockQueryResult({
      data: {
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
          {
            id: "ev-2",
            eventType: "Incremental",
            success: false,
            inserted: 0,
            updated: 0,
            reconciled: 0,
            duplicates: 0,
            errorMessage: "provider timeout",
            occurredAt: "2026-11-23T11:00:00Z",
          },
        ],
        total: 2,
      },
      isSuccess: true,
      isPending: false,
      isLoading: false,
    }),
  );
}

describe("TillerSyncCard", () => {
  it("uses the existing hourly sync instead of offering direct pull controls", () => {
    mockStatus();
    vi.mocked(useSyncTiller).mockReturnValue(mockMutationResult({}));
    renderWithQueryClient(<TillerSyncCard executorManaged />);
    expect(screen.getByText("Atlas refreshes this snapshot hourly.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /sync new rows|full re-sync/i })).toBeNull();
  });

  it("starts full and incremental syncs by mode", async () => {
    mockStatus();
    const mutate = vi.fn();
    vi.mocked(useSyncTiller).mockReturnValue(
      mockMutationResult({ mutate, isPending: false }),
    );
    const user = userEvent.setup();
    renderWithQueryClient(<TillerSyncCard />);
    expect(screen.getByText(/up to date/i)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /sync new rows/i }));
    await waitFor(() => expect(mutate).toHaveBeenCalledWith("incremental"));
    await user.click(screen.getByRole("button", { name: /full re-sync/i }));
    await waitFor(() => expect(mutate).toHaveBeenCalledWith("full"));
  });

  it("shows failed runs with their error", () => {
    mockStatus();
    vi.mocked(useSyncTiller).mockReturnValue(mockMutationResult({}));
    renderWithQueryClient(<TillerSyncCard />);
    const failed = screen.getByText("Failed");
    expect(failed).toBeInTheDocument();
    expect(failed.title).toBe("provider timeout");
  });
});
