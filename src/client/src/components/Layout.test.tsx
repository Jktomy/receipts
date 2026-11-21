import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { render } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AuthContext, type AuthContextValue } from "@/contexts/auth-context";
import { AppearanceProvider } from "@/contexts/AppearanceContext";
import {
  ShortcutsContext,
  type ShortcutsContextValue,
} from "@/contexts/shortcuts-context";
import { Layout } from "./Layout";

vi.mock("@/hooks/useSignalR", () => ({
  useSignalR: vi.fn(() => ({ connectionState: "connected" as const })),
}));

vi.mock("@/hooks/useGlobalShortcuts", () => ({
  useGlobalShortcuts: vi.fn(),
}));

vi.mock("@/hooks/useBreadcrumbs", () => ({
  useBreadcrumbs: vi.fn(() => []),
}));

vi.mock("@/components/ShortcutsHelp", () => ({
  ShortcutsHelp: () => <div data-testid="shortcuts-help" />,
}));

vi.mock("@/components/CommandPalette", () => ({
  CommandPalette: () => <div data-testid="command-palette" />,
}));

const defaultAuth: AuthContextValue = {
  user: {
    userId: "test-user-id",
    email: "test@example.com",
    roles: ["User"],
    mustResetPassword: false,
  },
  isLoading: false,
  mustResetPassword: false,
  login: async () => {},
  logout: async () => {},
  changePassword: async () => {},
};

const defaultShortcuts: ShortcutsContextValue = {
  helpOpen: false,
  setHelpOpen: vi.fn(),
};

// Layout mounts useLastRoutePersistence, which redirects "/" to the last route
// stored in localStorage. Without a reset, one test's route leaks into the next.
beforeEach(() => {
  localStorage.clear();
});

const adminUser = {
  userId: "admin-id",
  email: "admin@test.com",
  roles: ["Admin"],
  mustResetPassword: false,
};

function renderLayout(authOverrides?: Partial<AuthContextValue>, route = "/") {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
  const authValue = { ...defaultAuth, ...authOverrides };

  const router = createMemoryRouter(
    [
      {
        path: "/",
        element: <Layout />,
        children: [
          { index: true, element: <div>Home Page Content</div> },
          // Splat so any pathname under test renders without route-specific setup.
          { path: "*", element: <div>Page Content</div> },
        ],
      },
    ],
    { initialEntries: [route] },
  );

  return render(
    <QueryClientProvider client={queryClient}>
      <AppearanceProvider>
        <AuthContext.Provider value={authValue}>
          <ShortcutsContext.Provider value={defaultShortcuts}>
            <RouterProvider router={router} />
          </ShortcutsContext.Provider>
        </AuthContext.Provider>
      </AppearanceProvider>
    </QueryClientProvider>,
  );
}

describe("Layout", () => {
  it("renders the app brand name", () => {
    renderLayout();
    expect(screen.getAllByText("Tracer").length).toBeGreaterThan(0);
  });
