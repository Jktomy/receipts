import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { createQueryWrapper } from "@/test/test-utils";
import {
  useSyncTiller,
  useTillerSyncEvents,
  useTillerSyncStatus,
} from "./useTillerSync";

vi.mock("@/lib/tiller-api", () => ({
  getTillerSyncStatus: vi.fn(),
  listTillerSyncEvents: vi.fn(),
  syncTiller: vi.fn(),
}));

describe("useTillerSync", () => {
  it("reads status and history", async () => {
    const api = await import("@/lib/tiller-api");
    vi.mocked(api.getTillerSyncStatus).mockResolvedValue({
      spreadsheetId: "sheet-1",
      syncedThroughDate: "2026-11-22",
      completed: true,
      lastSuccessfulSyncUtc: null,
    });
    vi.mocked(api.listTillerSyncEvents).mockResolvedValue({ rows: [], total: 0 });
    const status = renderHook(() => useTillerSyncStatus(), {
      wrapper: createQueryWrapper(),
    });
    const events = renderHook(() => useTillerSyncEvents(0, 10), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(status.result.current.isSuccess).toBe(true));
    await waitFor(() => expect(events.result.current.isSuccess).toBe(true));
    expect(api.listTillerSyncEvents).toHaveBeenCalledWith(0, 10, undefined, expect.anything());
  });

  it("syncs by mode", async () => {
    const api = await import("@/lib/tiller-api");
    vi.mocked(api.syncTiller).mockResolvedValue({
      inserted: 1,
      updated: 0,
      reconciled: 0,
      duplicates: 0,
      invalid: 0,
      pages: 1,
    });
    const { result } = renderHook(() => useSyncTiller(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate("full");
    await waitFor(() => expect(api.syncTiller).toHaveBeenCalledWith("full"));
  });
});
