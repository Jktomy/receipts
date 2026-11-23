import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { createQueryWrapper } from "@/test/test-utils";
import { useTillerConnection } from "./useTillerConnection";

vi.mock("@/lib/tiller-api", () => ({
  getTillerConnectionStatus: vi.fn(),
}));

describe("useTillerConnection", () => {
  it("surfaces the configured-and-connected state", async () => {
    const { getTillerConnectionStatus } = await import("@/lib/tiller-api");
    vi.mocked(getTillerConnectionStatus).mockResolvedValue({
      isConfigured: true,
      isConnected: true,
      selectedSpreadsheetId: "sheet-1",
      lastSuccessfulSyncUtc: "2026-11-23T12:00:00Z",
    });
    const { result } = renderHook(() => useTillerConnection(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toMatchObject({ isConfigured: true });
  });

  it("surfaces the unconfigured state without inventing a spreadsheet", async () => {
    const { getTillerConnectionStatus } = await import("@/lib/tiller-api");
    vi.mocked(getTillerConnectionStatus).mockResolvedValue({
      isConfigured: false,
      isConnected: false,
      selectedSpreadsheetId: null,
      lastSuccessfulSyncUtc: null,
    });
    const { result } = renderHook(() => useTillerConnection(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.selectedSpreadsheetId).toBeNull();
  });

  it("surfaces transport errors for retry", async () => {
    const { getTillerConnectionStatus } = await import("@/lib/tiller-api");
    vi.mocked(getTillerConnectionStatus).mockRejectedValue(
      new Error("network down"),
    );
    const { result } = renderHook(() => useTillerConnection(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
