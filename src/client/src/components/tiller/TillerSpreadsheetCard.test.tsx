import { beforeAll, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import "@/test/setup-combobox-polyfills";

beforeAll(() => {
  Element.prototype.hasPointerCapture ??= () => false;
  Element.prototype.setPointerCapture ??= () => {};
  Element.prototype.releasePointerCapture ??= () => {};
});
import { renderWithQueryClient } from "@/test/test-utils";
import { mockMutationResult, mockQueryResult } from "@/test/mock-hooks";
import { TillerSpreadsheetCard } from "./TillerSpreadsheetCard";
import {
  useSelectedTillerSpreadsheet,
  useSelectTillerSpreadsheet,
  useTillerSpreadsheets,
} from "@/hooks/useTillerSpreadsheets";

vi.mock("@/hooks/useTillerSpreadsheets", () => ({
  useTillerSpreadsheets: vi.fn(),
  useSelectedTillerSpreadsheet: vi.fn(),
  useSelectTillerSpreadsheet: vi.fn(),
}));

const sheets = [
  {
      spreadsheetId: "s-1",
      title: "Budget 2026",
      editable: true,
      backendAutoFill: true,
      accountCount: 1,
    },
];

function mockAll() {
  vi.mocked(useTillerSpreadsheets).mockReturnValue(
    mockQueryResult({ data: sheets, isSuccess: true, isPending: false, isLoading: false }),
  );
  vi.mocked(useSelectedTillerSpreadsheet).mockReturnValue(
    mockQueryResult({
      data: { selectedSpreadsheetId: null },
      isSuccess: true,
      isPending: false,
      isLoading: false,
    }),
  );
}

describe("TillerSpreadsheetCard", () => {
  it("shows neutral guidance when no spreadsheets exist", () => {
    vi.mocked(useTillerSpreadsheets).mockReturnValue(
      mockQueryResult({ data: [], isSuccess: true, isPending: false, isLoading: false }),
    );
    vi.mocked(useSelectedTillerSpreadsheet).mockReturnValue(
      mockQueryResult({
      data: { selectedSpreadsheetId: null },
      isSuccess: true,
      isPending: false,
      isLoading: false,
    }),
    );
    vi.mocked(useSelectTillerSpreadsheet).mockReturnValue(
      mockMutationResult({}),
    );
    renderWithQueryClient(<TillerSpreadsheetCard />);
    expect(screen.getByText(/no tiller spreadsheets found/i)).toBeInTheDocument();
  });

  it("selects by spreadsheet id", async () => {
    mockAll();
    const mutate = vi.fn();
    vi.mocked(useSelectTillerSpreadsheet).mockReturnValue(
      mockMutationResult({ mutate, isPending: false }),
    );
    const user = userEvent.setup();
    renderWithQueryClient(<TillerSpreadsheetCard />);
    await user.click(screen.getByRole("combobox", { name: /tiller spreadsheet/i }));
    await user.click(screen.getByRole("option", { name: "Budget 2026" }));
    await user.click(screen.getByRole("button", { name: /use spreadsheet/i }));
    await waitFor(() => expect(mutate).toHaveBeenCalledWith("s-1"));
  });
});
