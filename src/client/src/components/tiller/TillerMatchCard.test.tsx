import { describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithQueryClient } from "@/test/test-utils";
import { mockMutationResult, mockQueryResult } from "@/test/mock-hooks";
import { TillerMatchCard } from "./TillerMatchCard";
import {
  useConfirmTillerLink,
  useTillerLinkHistory,
  useTillerMatchSuggestion,
  useUnlinkTillerLink,
} from "@/hooks/useTillerMatching";

vi.mock("@/hooks/useTillerMatching", () => ({
  useTillerMatchSuggestion: vi.fn(),
  useTillerLinkHistory: vi.fn(),
  useTillerPaymentContext: vi.fn(),
  useConfirmTillerLink: vi.fn(),
  useUnlinkTillerLink: vi.fn(),
}));

const ambiguous = {
  decision: "ambiguous",
  receiptId: null,
  confidence: null,
  candidates: [
    { receiptId: "receipt-1", providerTransactionId: "txn-7", dateWithinDays: 1 },
  ],
};

const history = [
  { action: "auto-link", receiptId: "receipt-9", reviewer: null, at: "2026-11-22T10:00:00Z" },
  { action: "unlink", receiptId: null, reviewer: "anna", at: "2026-11-22T11:00:00Z" },
];

function mockSuggestion(suggestion: unknown) {
  vi.mocked(useTillerMatchSuggestion).mockReturnValue(
    mockQueryResult({ data: suggestion, isSuccess: true, isPending: false, isLoading: false }),
  );
  vi.mocked(useTillerLinkHistory).mockReturnValue(
    mockQueryResult({ data: history, isSuccess: true, isPending: false, isLoading: false }),
  );
}

describe("TillerMatchCard", () => {
  it("confirms a candidate with reviewer attribution", async () => {
    mockSuggestion(ambiguous);
    const mutate = vi.fn();
    vi.mocked(useConfirmTillerLink).mockReturnValue(
      mockMutationResult({ mutate, isPending: false }),
    );
    vi.mocked(useUnlinkTillerLink).mockReturnValue(mockMutationResult({}));
    const user = userEvent.setup();
    renderWithQueryClient(<TillerMatchCard providerTransactionId="txn-7" />);
    await user.type(screen.getByLabelText("Reviewer"), "anna");
    await user.click(screen.getByRole("button", { name: "Link" }));
    await waitFor(() =>
      expect(mutate).toHaveBeenCalledWith({
        providerTransactionId: "txn-7",
        receiptId: "receipt-1",
        reviewer: "anna",
      }),
    );
  });

  it("unlinks with history preserved", async () => {
    mockSuggestion({ ...ambiguous, decision: "linked", receiptId: "receipt-1" });
    vi.mocked(useConfirmTillerLink).mockReturnValue(mockMutationResult({}));
    const mutate = vi.fn();
    vi.mocked(useUnlinkTillerLink).mockReturnValue(
      mockMutationResult({ mutate, isPending: false }),
    );
    const user = userEvent.setup();
    renderWithQueryClient(<TillerMatchCard providerTransactionId="txn-7" />);
    expect(screen.getByText("Linked.")).toBeInTheDocument();
    expect(screen.getByText(/auto-link/)).toBeInTheDocument();
    expect(screen.getByText(/by anna/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /unlink/i }));
    await waitFor(() =>
      expect(mutate).toHaveBeenCalledWith({
        providerTransactionId: "txn-7",
        reviewer: undefined,
      }),
    );
  });

  it("stays neutral when nothing is linked", () => {
    mockSuggestion({ decision: "none", receiptId: null, confidence: null, candidates: [] });
    vi.mocked(useConfirmTillerLink).mockReturnValue(mockMutationResult({}));
    vi.mocked(useUnlinkTillerLink).mockReturnValue(mockMutationResult({}));
    renderWithQueryClient(<TillerMatchCard providerTransactionId="txn-7" />);
    expect(screen.getByText(/not linked — normal/i)).toBeInTheDocument();
  });
});
