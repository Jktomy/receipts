import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test/test-utils";
import PaperlessInbox from "./PaperlessInbox";

vi.mock("@/hooks/usePageTitle", () => ({
  usePageTitle: vi.fn(),
}));

type BridgeState = {
  rev: number;
  confirmed: string | null;
  lastPut: unknown;
  lastImport: unknown;
  lastConfirm: unknown;
};

function draftPayload(rev: number, confirmed: string | null) {
  return {
    paperlessId: 44,
    status: "READY_FOR_REVIEW",
    problems: [],
    receiptId: null,
    draftRevision: rev,
    confirmedFireflyTransactionId: confirmed,
    draft: {
      merchant: "H-E-B",
      date: "2026-11-18",
      total: "8.60",
      tax: "0.61",
      currency: "USD",
      confidence: "high",
      items: [
        {
          description: "Milk",
          quantity: "1",
          unitPrice: "3.99",
          total: "3.99",
          evidence: "MILK 1 3.99",
        },
      ],
      adjustments: [],
    },
  };
}

function installBridge(state: BridgeState, matchStatus = "UNMATCHED") {
  const json = (data: unknown, status = 200) =>
    new Response(JSON.stringify(data), { status });
  return vi
    .spyOn(globalThis, "fetch")
    .mockImplementation(
      async (input: string | URL | Request, init?: RequestInit) => {
        const url = String(input);
        const method = init?.method ?? "GET";
        const body = init?.body
          ? (JSON.parse(init.body as string) as Record<string, unknown>)
          : undefined;
        if (url.endsWith("/api/atlas/paperless/status"))
          return json({ configured: true, paperlessBrowserUrl: null });
        if (url.endsWith("/api/atlas/paperless/drafts") && method === "GET") {
          return json({
            drafts: [
              {
                paperlessId: 44,
                status: "READY_FOR_REVIEW",
                modified: "2026-11-18",
                problems: [],
                receiptId: null,
                merchant: "H-E-B",
                date: "2026-11-18",
                total: "8.60",
                draftRevision: state.rev,
                confirmedFireflyTransactionId: state.confirmed,
              },
            ],
          });
        }
        if (url.endsWith("/api/atlas/paperless/drafts/44") && method === "GET")
          return json(draftPayload(state.rev, state.confirmed));
        if (
          url.endsWith("/api/atlas/paperless/drafts/44") &&
          method === "PUT"
        ) {
          state.rev += 1;
          state.lastPut = body;
          return json({ status: "DRAFT_UPDATED", draftRevision: state.rev });
        }
        if (url.endsWith("/api/atlas/paperless/drafts/44/import")) {
          state.lastImport = body;
          return json({ status: "IMPORTED", receiptId: "receipts-101" });
        }
        if (url.endsWith("/api/atlas/paperless/drafts/44/match")) {
          return json(
            matchStatus === "PROPOSED_MATCH"
              ? { status: "PROPOSED_MATCH", fireflyTransactionId: "31" }
              : {
                  status: "UNMATCHED",
                  reason: "No account/amount/date candidate",
                },
          );
        }
        if (url.endsWith("/api/atlas/paperless/drafts/44/confirm-match")) {
          state.lastConfirm = body;
          state.confirmed = body?.fireflyTransactionId as string;
          return json({
            status: "MATCH_CONFIRMED",
            fireflyTransactionId: state.confirmed,
          });
        }
        return json({ error: "not found" }, 404);
      },
    );
}

beforeEach(() => {
  localStorage.setItem("receipts_access_token", "test-token");
  vi.spyOn(window, "confirm").mockReturnValue(true);
});

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
});

async function openDraft() {
  renderWithProviders(<PaperlessInbox />);
  const user = userEvent.setup();
  const row = await screen.findByRole("button", { name: /H-E-B/ });
  await user.click(row);
  await screen.findByText("Paperless document #44");
  return user;
}

describe("PaperlessInbox", () => {
  it("lists drafts and opens a review", async () => {
    const state: BridgeState = {
      rev: 1,
      confirmed: null,
      lastPut: null,
      lastImport: null,
      lastConfirm: null,
    };
    installBridge(state);
    await openDraft();
    expect(screen.getByText(/Draft revision 1/)).toBeInTheDocument();
  });

  it("edits item units and sends them with corrections", async () => {
    const state: BridgeState = {
      rev: 1,
      confirmed: null,
      lastPut: null,
      lastImport: null,
      lastConfirm: null,
    };
    installBridge(state);
    const user = await openDraft();
    const unit = screen.getByLabelText("Item 1 unit");
    expect(unit).toBeInTheDocument();
    await user.clear(unit);
    await user.type(unit, "gal");
    await user.click(screen.getByRole("button", { name: "Save corrections" }));
    await waitFor(() =>
      expect(screen.getByText(/Corrections saved/)).toBeInTheDocument(),
    );
    expect(
      (state.lastPut as { items: { unit?: string }[] }).items[0].unit,
    ).toBe("gal");
  });

  it("adds and removes items, then saves corrections", async () => {
    const state: BridgeState = {
      rev: 1,
      confirmed: null,
      lastPut: null,
      lastImport: null,
      lastConfirm: null,
    };
    installBridge(state);
    const user = await openDraft();
    expect(screen.getByLabelText("Item 1 description")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Add item" }));
    expect(screen.getByLabelText("Item 2 description")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Remove item 2" }));
    expect(
      screen.queryByLabelText("Item 2 description"),
    ).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Save corrections" }));
    await waitFor(() =>
      expect(screen.getByText(/Corrections saved/)).toBeInTheDocument(),
    );
    expect(state.rev).toBe(2);
    expect((state.lastPut as { items: unknown[] }).items).toHaveLength(1);
  });

  it("edits adjustments and sends the reviewed revision on approval", async () => {
    const state: BridgeState = {
      rev: 2,
      confirmed: null,
      lastPut: null,
      lastImport: null,
      lastConfirm: null,
    };
    installBridge(state);
    const user = await openDraft();
    await user.click(screen.getByRole("button", { name: "Add adjustment" }));
    await user.type(screen.getByLabelText("Adjustment 1 type"), "coupon");
    await user.type(screen.getByLabelText("Adjustment 1 amount"), "-1.00");
    await user.click(screen.getByRole("button", { name: "Save corrections" }));
    await waitFor(() =>
      expect(screen.getByText(/Corrections saved/)).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /Approve/ }));
    await waitFor(() =>
      expect(screen.getByText(/Imported to Receipts/)).toBeInTheDocument(),
    );
    expect(state.lastImport).toEqual({ expectedRevision: 3 });
  });

  it("sends the edited revision when saving corrections", async () => {
    const state: BridgeState = {
      rev: 1,
      confirmed: null,
      lastPut: null,
      lastImport: null,
      lastConfirm: null,
    };
    installBridge(state);
    const user = await openDraft();
    await user.type(screen.getByLabelText("Item 1 unit"), "gal");
    await user.click(screen.getByRole("button", { name: "Save corrections" }));
    await waitFor(() =>
      expect(screen.getByText(/Corrections saved/)).toBeInTheDocument(),
    );
    expect((state.lastPut as { expectedRevision?: number }).expectedRevision).toBe(1);
  });

  it("requires printed-line evidence for added items before approval", async () => {
    const state: BridgeState = {
      rev: 1,
      confirmed: null,
      lastPut: null,
      lastImport: null,
      lastConfirm: null,
    };
    installBridge(state);
    const user = await openDraft();
    await user.click(screen.getByRole("button", { name: "Add item" }));
    expect(screen.getByLabelText("Item 2 evidence")).toBeInTheDocument();
    expect(screen.getByLabelText("Item 2 package content quantity")).toBeInTheDocument();
    expect(screen.getByLabelText("Item 2 package content unit")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Save corrections" }));
    await waitFor(() =>
      expect(screen.getByText(/Corrections saved/)).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /Approve/ }));
    expect(await screen.findByText(/needs its exact printed receipt line/)).toBeInTheDocument();
    expect(state.lastImport).toBeNull();
    await user.type(screen.getByLabelText("Item 2 description"), "Eggs");
    await user.type(screen.getByLabelText("Item 2 unitPrice"), "5.00");
    await user.type(screen.getByLabelText("Item 2 total"), "5.00");
    await user.type(screen.getByLabelText("Item 2 evidence"), "EGGS 1 5.00");
    await user.click(screen.getByRole("button", { name: "Save corrections" }));
    await waitFor(() =>
      expect(screen.getByText(/Corrections saved/)).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /Approve/ }));
    await waitFor(() =>
      expect(screen.getByText(/Imported to Receipts/)).toBeInTheDocument(),
    );
    expect(state.lastImport).toEqual({ expectedRevision: 3 });
  });

  it("shows neutral wording when no payment is linked", async () => {
    const state: BridgeState = {
      rev: 1,
      confirmed: null,
      lastPut: null,
      lastImport: null,
      lastConfirm: null,
    };
    installBridge(state, "UNMATCHED");
    const user = await openDraft();
    await user.type(screen.getByLabelText("Firefly source account ID"), "9");
    await user.click(screen.getByRole("button", { name: "Find payment" }));
    await screen.findByText(/No payment linked/);
    expect(
      screen.queryByRole("button", { name: "Confirm payment link" }),
    ).not.toBeInTheDocument();
  });

  it("confirms a proposed payment link without changing Firefly", async () => {
    const state: BridgeState = {
      rev: 1,
      confirmed: null,
      lastPut: null,
      lastImport: null,
      lastConfirm: null,
    };
    installBridge(state, "PROPOSED_MATCH");
    const user = await openDraft();
    await user.type(screen.getByLabelText("Firefly source account ID"), "9");
    await user.click(screen.getByRole("button", { name: "Find payment" }));
    await user.click(
      await screen.findByRole("button", { name: "Confirm payment link" }),
    );
    await waitFor(() =>
      expect(screen.getByText(/Payment link confirmed/)).toBeInTheDocument(),
    );
    expect(state.lastConfirm).toEqual({
      fireflyTransactionId: "31",
      fireflySourceAccountId: "9",
    });
    await within(
      screen.getByRole("region", { name: "Review selected receipt" }),
    ).findByText(/Confirmed payment: Firefly transaction #31/);
  });
});
