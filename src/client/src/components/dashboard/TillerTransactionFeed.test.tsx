import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithQueryClient } from "@/test/test-utils";
import { listCachedTillerTransactions } from "@/lib/tiller-api";
import { TillerTransactionFeed } from "./TillerTransactionFeed";
import type { TillerStoredTransaction } from "@/lib/tiller-contracts";

vi.mock("@/lib/tiller-api", () => ({ listCachedTillerTransactions: vi.fn() }));

const row: TillerStoredTransaction = {
  id: "row-1", providerTransactionId: "provider-1", date: "2026-01-02",
  description: "Synthetic merchant", amount: -12.5, category: "Groceries",
  account: "Synthetic account", institution: "Synthetic bank", status: "posted",
  splitGroupId: null, revision: 1, linkKind: null, linkedReceiptId: null,
};

beforeEach(() => { vi.clearAllMocks(); });

it("shows charges without receipts and links existing receipt records", async () => {
  vi.mocked(listCachedTillerTransactions).mockResolvedValue({ total: 2, rows: [
    row, { ...row, id: "row-2", providerTransactionId: "provider-2",
      description: "Linked merchant", linkedReceiptId: "receipt-1", linkKind: "manual" },
  ] });
  renderWithQueryClient(<TillerTransactionFeed />);
  expect(await screen.findByText("Synthetic merchant")).toBeInTheDocument();
  expect(screen.getByText("Not linked")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Linked receipt" })).toHaveAttribute("href", "/receipts/receipt-1");
  expect(screen.getByText("2 cached transactions")).toBeInTheDocument();
});

it("pages the cache and resets pagination when filtering unlinked rows", async () => {
  vi.mocked(listCachedTillerTransactions).mockResolvedValue({ total: 25, rows: [row] });
  renderWithQueryClient(<TillerTransactionFeed />);
  await screen.findByText("Synthetic merchant");
  await userEvent.click(screen.getByRole("button", { name: "Next" }));
  await waitFor(() => expect(listCachedTillerTransactions).toHaveBeenCalledWith(10, 10, false, expect.anything()));
  await screen.findByText("Synthetic merchant");
  await userEvent.click(screen.getByRole("checkbox", { name: "Without linked receipt only" }));
  await waitFor(() => expect(listCachedTillerTransactions).toHaveBeenCalledWith(0, 10, true, expect.anything()));
});

it("shows an unavailable state rather than a false zero", async () => {
  vi.mocked(listCachedTillerTransactions).mockRejectedValue(new Error("Unavailable"));
  renderWithQueryClient(<TillerTransactionFeed />);
  expect(await screen.findByRole("alert")).toHaveTextContent("Bank activity unavailable");
  expect(screen.queryByText("0 cached transactions")).not.toBeInTheDocument();
});
