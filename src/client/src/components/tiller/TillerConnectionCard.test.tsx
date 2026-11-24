import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithQueryClient } from "@/test/test-utils";
import { mockQueryResult } from "@/test/mock-hooks";
import { TillerConnectionCard } from "./TillerConnectionCard";
import { useTillerConnection } from "@/hooks/useTillerConnection";

vi.mock("@/hooks/useTillerConnection", () => ({
  useTillerConnection: vi.fn(),
}));

describe("TillerConnectionCard", () => {
  it("explains the unconfigured state without asking for secrets", () => {
    vi.mocked(useTillerConnection).mockReturnValue(
      mockQueryResult({
        data: {
          isConfigured: false,
          isConnected: false,
          selectedSpreadsheetId: null,
          lastSuccessfulSyncUtc: null,
        },
        isSuccess: true,
        isPending: false,
        isLoading: false,
      }),
    );
    renderWithQueryClient(<TillerConnectionCard />);
    expect(screen.getByText("Not connected")).toBeInTheDocument();
    expect(screen.queryByLabelText(/password|secret|token|key/i)).toBeNull();
  });

  it("offers retry when disconnected", async () => {
    const refetch = vi.fn();
    vi.mocked(useTillerConnection).mockReturnValue(
      mockQueryResult({
        data: {
          isConfigured: true,
          isConnected: false,
          selectedSpreadsheetId: "s-1",
          lastSuccessfulSyncUtc: null,
        },
        isSuccess: true,
        isPending: false,
        isLoading: false,
        refetch,
      }),
    );
    const user = userEvent.setup();
    renderWithQueryClient(<TillerConnectionCard />);
    expect(screen.getByText(/connection failed/i)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /retry/i }));
    expect(refetch).toHaveBeenCalled();
  });

  it("summarizes the connected state", () => {
    vi.mocked(useTillerConnection).mockReturnValue(
      mockQueryResult({
        data: {
          isConfigured: true,
          isConnected: true,
          selectedSpreadsheetId: "s-1",
          lastSuccessfulSyncUtc: "2026-11-23T12:00:00Z",
        },
        isSuccess: true,
        isPending: false,
        isLoading: false,
      }),
    );
    renderWithQueryClient(<TillerConnectionCard />);
    expect(screen.getByText("Connected")).toBeInTheDocument();
    expect(screen.getByText(/s-1/)).toBeInTheDocument();
  });

  it("recovers from status errors", async () => {
    const refetch = vi.fn();
    vi.mocked(useTillerConnection).mockReturnValue(
      mockQueryResult({ isError: true, isPending: false, refetch }),
    );
    const user = userEvent.setup();
    renderWithQueryClient(<TillerConnectionCard />);
    await user.click(screen.getByRole("button", { name: /retry/i }));
    expect(refetch).toHaveBeenCalled();
  });
});
