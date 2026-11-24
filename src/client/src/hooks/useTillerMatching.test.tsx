import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { createQueryWrapper } from "@/test/test-utils";
import {
  useConfirmTillerLink,
  useTillerLinkHistory,
  useTillerMatchSuggestion,
  useTillerPaymentContext,
  useUnlinkTillerLink,
} from "./useTillerMatching";

import type { TillerMatchSuggestion } from "@/lib/tiller-contracts";

vi.mock("@/lib/tiller-api", () => ({
  getTillerMatchSuggestion: vi.fn(),
  getTillerLinkHistory: vi.fn(),
  getTillerPaymentContext: vi.fn(),
  confirmTillerLink: vi.fn(),
  unlinkTillerLink: vi.fn(),
}));

const suggestion: TillerMatchSuggestion = {
  decision: "ambiguous",
  receiptId: null,
  confidence: null,
  candidates: [],
};

describe("useTillerMatching", () => {
  it("reads suggestion, history and payment context", async () => {
    const api = await import("@/lib/tiller-api");
    vi.mocked(api.getTillerMatchSuggestion).mockResolvedValue(suggestion);
    vi.mocked(api.getTillerLinkHistory).mockResolvedValue([]);
    vi.mocked(api.getTillerPaymentContext).mockResolvedValue(null);
    const s = renderHook(() => useTillerMatchSuggestion("txn-7"), {
      wrapper: createQueryWrapper(),
    });
    const h = renderHook(() => useTillerLinkHistory("txn-7"), {
      wrapper: createQueryWrapper(),
    });
    const p = renderHook(() => useTillerPaymentContext("receipt-1"), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(s.result.current.isSuccess).toBe(true));
    await waitFor(() => expect(h.result.current.isSuccess).toBe(true));
    await waitFor(() => expect(p.result.current.isSuccess).toBe(true));
    expect(api.getTillerMatchSuggestion).toHaveBeenCalledWith(
      "txn-7",
      expect.anything(),
    );
  });

  it("does not fetch without an id", async () => {
    const api = await import("@/lib/tiller-api");
    vi.clearAllMocks();
    renderHook(() => useTillerMatchSuggestion(null), {
      wrapper: createQueryWrapper(),
    });
    renderHook(() => useTillerPaymentContext(null), {
      wrapper: createQueryWrapper(),
    });
    expect(api.getTillerMatchSuggestion).not.toHaveBeenCalled();
    expect(api.getTillerPaymentContext).not.toHaveBeenCalled();
  });

  it("confirms and unlinks with reviewer attribution", async () => {
    const api = await import("@/lib/tiller-api");
    vi.mocked(api.confirmTillerLink).mockResolvedValue(suggestion);
    vi.mocked(api.unlinkTillerLink).mockResolvedValue(suggestion);
    const confirm = renderHook(() => useConfirmTillerLink(), {
      wrapper: createQueryWrapper(),
    });
    const unlink = renderHook(() => useUnlinkTillerLink(), {
      wrapper: createQueryWrapper(),
    });
    confirm.result.current.mutate({
      providerTransactionId: "txn-7",
      receiptId: "receipt-1",
      reviewer: "anna",
    });
    await waitFor(() =>
      expect(api.confirmTillerLink).toHaveBeenCalledWith({
        providerTransactionId: "txn-7",
        receiptId: "receipt-1",
        reviewer: "anna",
      }),
    );
    unlink.result.current.mutate({ providerTransactionId: "txn-7" });
    await waitFor(() =>
      expect(api.unlinkTillerLink).toHaveBeenCalledWith({
        providerTransactionId: "txn-7",
      }),
    );
  });
});
