import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { createQueryWrapper } from "@/test/test-utils";
import {
  useCreateTillerAccountMapping,
  useDeleteTillerAccountMapping,
  useTillerAccountMappings,
  useTillerAccounts,
  useUpdateTillerAccountMapping,
} from "./useTillerAccountMappings";

vi.mock("@/lib/tiller-api", () => ({
  listTillerAccountMappings: vi.fn(),
  listTillerAccounts: vi.fn(),
  createTillerAccountMapping: vi.fn(),
  updateTillerAccountMapping: vi.fn(),
  deleteTillerAccountMapping: vi.fn(),
}));

const mapping = {
  receiptsAccountId: "acct-1",
  tillerAccountId: "t-99",
  tillerAccountName: "Checking",
  spreadsheetId: "sheet-1",
};

describe("useTillerAccountMappings", () => {
  it("lists mappings and accounts", async () => {
    const api = await import("@/lib/tiller-api");
    vi.mocked(api.listTillerAccountMappings).mockResolvedValue([
      { id: "map-1", ...mapping },
    ]);
    vi.mocked(api.listTillerAccounts).mockResolvedValue([
      {
        accountId: "t-99",
        displayName: "Checking",
        institution: "Bank",
        accountType: "checking",
        class: "asset",
      },
    ]);
    const mappings = renderHook(() => useTillerAccountMappings(), {
      wrapper: createQueryWrapper(),
    });
    const accounts = renderHook(() => useTillerAccounts(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(mappings.result.current.isSuccess).toBe(true));
    await waitFor(() => expect(accounts.result.current.isSuccess).toBe(true));
    expect(mappings.result.current.data).toHaveLength(1);
  });

  it("creates with the serialized command shape", async () => {
    const api = await import("@/lib/tiller-api");
    vi.mocked(api.createTillerAccountMapping).mockResolvedValue({
      id: "map-2",
      ...mapping,
    });
    const { result } = renderHook(() => useCreateTillerAccountMapping(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate(mapping);
    await waitFor(() =>
      expect(api.createTillerAccountMapping).toHaveBeenCalledWith(mapping),
    );
  });

  it("updates with id plus mutable fields", async () => {
    const api = await import("@/lib/tiller-api");
    vi.mocked(api.updateTillerAccountMapping).mockResolvedValue(undefined);
    const { result } = renderHook(() => useUpdateTillerAccountMapping(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({ id: "map-1", ...mapping });
    await waitFor(() =>
      expect(api.updateTillerAccountMapping).toHaveBeenCalledWith({
        id: "map-1",
        ...mapping,
      }),
    );
  });

  it("deletes by id", async () => {
    const api = await import("@/lib/tiller-api");
    vi.mocked(api.deleteTillerAccountMapping).mockResolvedValue(undefined);
    const { result } = renderHook(() => useDeleteTillerAccountMapping(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate("map-1");
    await waitFor(() =>
      expect(api.deleteTillerAccountMapping).toHaveBeenCalledWith("map-1"),
    );
  });
});
