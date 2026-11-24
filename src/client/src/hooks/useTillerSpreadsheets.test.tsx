import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { createQueryWrapper } from "@/test/test-utils";
import {
  useSelectTillerSpreadsheet,
  useSelectedTillerSpreadsheet,
  useTillerSpreadsheets,
} from "./useTillerSpreadsheets";

vi.mock("@/lib/tiller-api", () => ({
  listTillerSpreadsheets: vi.fn(),
  getSelectedTillerSpreadsheet: vi.fn(),
  selectTillerSpreadsheet: vi.fn(),
}));

describe("useTillerSpreadsheets", () => {
  it("lists spreadsheets", async () => {
    const api = await import("@/lib/tiller-api");
    vi.mocked(api.listTillerSpreadsheets).mockResolvedValue([
      {
        spreadsheetId: "s-1",
        title: "Budget",
        editable: true,
        backendAutoFill: true,
        accountCount: 3,
      },
    ]);
    const { result } = renderHook(() => useTillerSpreadsheets(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toHaveLength(1);
  });

  it("reads the selected spreadsheet", async () => {
    const api = await import("@/lib/tiller-api");
    vi.mocked(api.getSelectedTillerSpreadsheet).mockResolvedValue({
      selectedSpreadsheetId: "s-1",
    });
    const { result } = renderHook(() => useSelectedTillerSpreadsheet(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({ selectedSpreadsheetId: "s-1" });
  });

  it("selects by spreadsheet id", async () => {
    const api = await import("@/lib/tiller-api");
    vi.mocked(api.selectTillerSpreadsheet).mockResolvedValue({
      selectedSpreadsheetId: "s-2",
    });
    const { result } = renderHook(() => useSelectTillerSpreadsheet(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate("s-2");
    await waitFor(() =>
      expect(api.selectTillerSpreadsheet).toHaveBeenCalledWith("s-2"),
    );
  });
});
