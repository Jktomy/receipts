import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithQueryClient } from "@/test/test-utils";
import { mockQueryResult } from "@/test/mock-hooks";
import { TillerPaymentContextCard } from "./TillerPaymentContextCard";
import { useTillerPaymentContext } from "@/hooks/useTillerMatching";

vi.mock("@/hooks/useTillerMatching", () => ({
  useTillerPaymentContext: vi.fn(),
}));

describe("TillerPaymentContextCard", () => {
  it("shows the linked bank row as context", () => {
    vi.mocked(useTillerPaymentContext).mockReturnValue(
      mockQueryResult({
        data: {
          providerTransactionId: "txn-7",
          date: "2026-11-20",
          description: "WHOLEFDS MKT",
          amount: 42.18,
          account: "Checking",
          category: "Groceries",
          linkKind: "manual",
        },
        isSuccess: true,
        isPending: false,
        isLoading: false,
      }),
    );
    renderWithQueryClient(<TillerPaymentContextCard receiptId="receipt-1" />);
    expect(screen.getByText("WHOLEFDS MKT")).toBeInTheDocument();
    expect(screen.getByText("Checking")).toBeInTheDocument();
  });

  it("stays neutral when nothing is linked", () => {
    vi.mocked(useTillerPaymentContext).mockReturnValue(
      mockQueryResult({ data: null, isSuccess: true, isPending: false, isLoading: false }),
    );
    renderWithQueryClient(<TillerPaymentContextCard receiptId="receipt-1" />);
    expect(screen.getByText(/no linked bank row/i)).toBeInTheDocument();
  });
});
