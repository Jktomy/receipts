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
import { TillerAccountMappingsCard } from "./TillerAccountMappingsCard";
import {
  useCreateTillerAccountMapping,
  useDeleteTillerAccountMapping,
  useTillerAccountMappings,
  useTillerAccounts,
} from "@/hooks/useTillerAccountMappings";
import { useSelectedTillerSpreadsheet } from "@/hooks/useTillerSpreadsheets";
import { useAllAccounts } from "@/hooks/useAccounts";

vi.mock("@/hooks/useTillerAccountMappings", () => ({
  useTillerAccountMappings: vi.fn(),
  useTillerAccounts: vi.fn(),
  useCreateTillerAccountMapping: vi.fn(),
  useUpdateTillerAccountMapping: vi.fn(),
  useDeleteTillerAccountMapping: vi.fn(),
}));
vi.mock("@/hooks/useTillerSpreadsheets", () => ({
  useSelectedTillerSpreadsheet: vi.fn(),
}));
vi.mock("@/hooks/useAccounts", () => ({
  useAllAccounts: vi.fn(),
}));

function mockQueries() {
  vi.mocked(useTillerAccountMappings).mockReturnValue(
    mockQueryResult({
      data: [
        {
          id: "map-1",
          receiptsAccountId: "acct-1",
          tillerAccountId: "t-99",
          tillerAccountName: "Checking",
          spreadsheetId: "sheet-1",
        },
      ],
      isSuccess: true,
      isPending: false,
      isLoading: false,
    }),
  );
  vi.mocked(useTillerAccounts).mockReturnValue(
    mockQueryResult({
      data: [
        {
        accountId: "t-99",
        displayName: "Checking",
        institution: "Bank",
        accountType: "checking",
        class: "asset",
      },
      ],
      isSuccess: true,
      isPending: false,
      isLoading: false,
    }),
  );
  vi.mocked(useAllAccounts).mockReturnValue(
    mockQueryResult({
      data: [{ id: "acct-1", name: "Everyday", isActive: true }],
      isSuccess: true,
      isPending: false,
      isLoading: false,
    }),
  );
  vi.mocked(useSelectedTillerSpreadsheet).mockReturnValue(
    mockQueryResult({
    data: { selectedSpreadsheetId: "sheet-1" },
    isSuccess: true,
    isPending: false,
    isLoading: false,
  }),
  );
}

describe("TillerAccountMappingsCard", () => {
  it("shows the linked read-only account catalog without creating accounts", () => {
    mockQueries();
    const mutate = vi.fn();
    vi.mocked(useCreateTillerAccountMapping).mockReturnValue(mockMutationResult({ mutate }));
    vi.mocked(useDeleteTillerAccountMapping).mockReturnValue(mockMutationResult({}));
    renderWithQueryClient(<TillerAccountMappingsCard executorManaged />);
    expect(screen.getByText("1 linked Tiller accounts · Read-only")).toBeInTheDocument();
    expect(screen.getByText("Bank")).toBeInTheDocument();
    expect(mutate).not.toHaveBeenCalled();
  });

  it("creates with the serialized mapping shape", async () => {
    mockQueries();
    const mutate = vi.fn();
    vi.mocked(useCreateTillerAccountMapping).mockReturnValue(
      mockMutationResult({ mutate, isPending: false }),
    );
    vi.mocked(useDeleteTillerAccountMapping).mockReturnValue(
      mockMutationResult({}),
    );
    const user = userEvent.setup();
    renderWithQueryClient(<TillerAccountMappingsCard />);
    await user.click(screen.getByRole("combobox", { name: /receipts account/i }));
    await user.click(screen.getByRole("option", { name: "Everyday" }));
    await user.click(screen.getByRole("combobox", { name: /tiller account/i }));
    await user.click(screen.getByRole("option", { name: /checking/i }));
    await user.click(screen.getByRole("button", { name: /map accounts/i }));
    await waitFor(() =>
      expect(mutate).toHaveBeenCalledWith({
        receiptsAccountId: "acct-1",
        tillerAccountId: "t-99",
        tillerAccountName: "Checking",
        spreadsheetId: "sheet-1",
      }),
    );
  });

  it("deletes by mapping id", async () => {
    mockQueries();
    vi.mocked(useCreateTillerAccountMapping).mockReturnValue(
      mockMutationResult({}),
    );
    const mutate = vi.fn();
    vi.mocked(useDeleteTillerAccountMapping).mockReturnValue(
      mockMutationResult({ mutate, isPending: false }),
    );
    const user = userEvent.setup();
    renderWithQueryClient(<TillerAccountMappingsCard />);
    await user.click(screen.getByRole("button", { name: /remove/i }));
    await waitFor(() => expect(mutate).toHaveBeenCalledWith("map-1"));
  });
});
