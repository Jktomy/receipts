import { Fragment, useCallback, useEffect, useState } from "react";
import { Link } from "react-router";
import { usePageTitle } from "@/hooks/usePageTitle";
import { PageHead } from "@/components/primitives";
import { apiUrl } from "@/lib/api-config";
import { getAccessToken, getSessionSignal, parseJwtPayload } from "@/lib/auth";

type AtlasItem = {
  description?: string;
  sku?: string;
  evidence?: string;
  quantity?: string;
  unit?: string;
  unitPrice?: string;
  total?: string;
  packageContentQuantity?: string;
  packageContentUnit?: string;
};
type AtlasAdjustment = { type?: string; amount?: string; description?: string };
type AtlasDraft = {
  merchant?: string;
  date?: string;
  total?: string;
  tax?: string;
  currency?: string;
  confidence?: string;
  items?: AtlasItem[];
  adjustments?: AtlasAdjustment[];
  [key: string]: unknown;
};
type DraftSummary = {
  paperlessId: number;
  status: string;
  modified?: string;
  problems: string[];
  receiptId?: string | null;
  draftRevision?: number | null;
  contentHash?: string | null;
  confirmedFireflyTransactionId?: string | null;
  merchant?: string | null;
  date?: string | null;
  total?: string | null;
  sourceStatus?: { status: string; checkedAt?: string } | null;
};
type DraftState = DraftSummary & { draft?: AtlasDraft; reason?: string };
type MatchResult = {
  status: "PROPOSED_MATCH" | "REVIEW" | "UNMATCHED";
  reason?: string;
  fireflyTransactionId?: string;
  candidateTransactionIds?: string[];
};

async function atlasApi<T>(path: `/api/atlas/paperless${string}`, method = "GET", body?: object, headers?: Record<string, string>): Promise<T> {
  const token = getAccessToken();
  if (!token) throw new Error("Sign in to Receipts to review imported documents");
  const response = await fetch(apiUrl(path), {
    method,
    cache: "no-store",
    credentials: "omit",
    signal: getSessionSignal(),
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: "application/json",
      ...(body ? { "Content-Type": "application/json" } : {}),
      ...headers,
    },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const data: unknown = await response.json().catch(() => null);
  if (!response.ok) {
    const problem = data && typeof data === "object" && "error" in data ? String(data.error) : "Request failed";
    throw new Error(problem);
  }
  return data as T;
}

/**
 * The reviewer's explicit approval of the exact draft on screen: identity from
 * the signed-in JWT, evidence bound to the OCR digest and draft revision the
 * bridge will re-check. The server rejects a stale or mismatched attestation.
 */
function reviewAttestationHeader(draft: DraftState): string | undefined {
  const token = getAccessToken();
  const claims = token ? parseJwtPayload(token) : null;
  if (!claims?.email || !draft.contentHash || draft.draftRevision == null) return undefined;
  const evidence = {
    reviewer: claims.email,
    approvedAt: new Date().toISOString(),
    ocrSha256: draft.contentHash,
    draftRevision: String(draft.draftRevision),
    finding: "Model did not attest high confidence",
  };
  return btoa(JSON.stringify(evidence));
}

function matchLabel(match: MatchResult): string {
  // A receipt without a Firefly payment is valid: neutral wording, never an error.
  if (match.status === "UNMATCHED") return "No payment linked";
  return match.status.replaceAll("_", " ");
}

function Field({ label, value, onChange }: {
  label: string; value?: string; onChange: (value: string) => void;
}) {
  return (
    <label className="block space-y-1 text-sm">
      <span className="font-medium">{label}</span>
      <input className="w-full rounded border bg-background px-3 py-2" value={value ?? ""} onChange={e => onChange(e.target.value)} />
    </label>
  );
}

const blankItem: AtlasItem = { description: "", quantity: "1", unit: "", unitPrice: "", total: "", evidence: "", sku: "", packageContentQuantity: "", packageContentUnit: "" };
const blankAdjustment: AtlasAdjustment = { type: "", amount: "", description: "" };

export default function PaperlessInbox() {
  usePageTitle("Paperless Review");
  const [configured, setConfigured] = useState(false);
  const [paperlessBrowserUrl, setPaperlessBrowserUrl] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [rows, setRows] = useState<DraftSummary[]>([]);
  const [selected, setSelected] = useState<DraftState | null>(null);
  const [draft, setDraft] = useState<AtlasDraft | null>(null);
  const [dirty, setDirty] = useState(false);
  const [message, setMessage] = useState<string>();
  const [error, setError] = useState<string>();
  const [sourceAccountId, setSourceAccountId] = useState("");
  const [match, setMatch] = useState<MatchResult | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      const status = await atlasApi<{ configured: boolean; paperlessBrowserUrl?: string | null }>("/api/atlas/paperless/status");
      setPaperlessBrowserUrl(status.paperlessBrowserUrl ?? null);
      setConfigured(status.configured);
      if (status.configured) {
        const result = await atlasApi<{ drafts: DraftSummary[] }>("/api/atlas/paperless/drafts");
        setRows(result.drafts);
      } else setRows([]);
      setError(undefined);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Cannot load Paperless inbox");
    } finally { setLoading(false); }
  }, []);

  useEffect(() => { void refresh(); }, [refresh]);

  async function select(id: number) {
    if (dirty && !window.confirm("Discard unsaved corrections?")) return;
    setBusy(true); setError(undefined); setMatch(null); setMessage(undefined);
    try {
      const data = await atlasApi<DraftState>(`/api/atlas/paperless/drafts/${id}`);
      setSelected(data); setDraft(data.draft ?? null); setDirty(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Cannot open draft");
    } finally { setBusy(false); }
  }
  function edit(patch: Partial<AtlasDraft>) {
    if (!draft) return;
    setDraft({ ...draft, ...patch }); setDirty(true); setMessage(undefined);
  }
  function editItem(index: number, key: keyof AtlasItem, value: string) {
    if (!draft) return;
    const items = [...(draft.items ?? [])];
    items[index] = { ...items[index], [key]: value };
    edit({ items });
  }
  function addItem() {
    if (!draft) return;
    edit({ items: [...(draft.items ?? []), { ...blankItem }] });
  }
  function removeItem(index: number) {
    if (!draft) return;
    edit({ items: (draft.items ?? []).filter((_, i) => i !== index) });
  }
  function editAdjustment(index: number, key: keyof AtlasAdjustment, value: string) {
    if (!draft) return;
    const adjustments = [...(draft.adjustments ?? [])];
    adjustments[index] = { ...adjustments[index], [key]: value };
    edit({ adjustments });
  }
  function addAdjustment() {
    if (!draft) return;
    edit({ adjustments: [...(draft.adjustments ?? []), { ...blankAdjustment }] });
  }
  function removeAdjustment(index: number) {
    if (!draft) return;
    edit({ adjustments: (draft.adjustments ?? []).filter((_, i) => i !== index) });
  }
  async function save() {
    if (!selected || !draft) return;
    if (selected.draftRevision == null) { setError("Reload the draft before saving corrections."); return; }
    setBusy(true); setError(undefined);
    try {
      // Compare-and-swap: name the revision edited so a concurrent reviewer save is rejected, not silently lost.
      const saved = await atlasApi<{ status: string; draftRevision: number; confirmationInvalidated?: boolean }>(`/api/atlas/paperless/drafts/${selected.paperlessId}`, "PUT", { ...draft, expectedRevision: selected.draftRevision });
      setDirty(false);
      setMessage(saved.confirmationInvalidated
        ? "Corrections saved; the previous payment link was cleared because the draft changed. Re-check before approval."
        : "Corrections saved. Re-run import validation before approval.");
      const state = await atlasApi<DraftState>(`/api/atlas/paperless/drafts/${selected.paperlessId}`);
      setSelected(state);
      await refresh();
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to save"); }
    finally { setBusy(false); }
  }
  async function importReceipt() {
    if (!selected || !draft || dirty) return;
    const missingEvidence = (draft.items ?? []).findIndex(item => !(item.evidence ?? "").trim());
    if (missingEvidence >= 0) { setError(`Item ${missingEvidence + 1} needs its exact printed receipt line before approval.`); return; }
    if (selected.draftRevision == null) { setError("Reload the draft before approving."); return; }
    if (!window.confirm("Import these reviewed line items into Receipts? This does NOT change Firefly.")) return;
    setBusy(true); setError(undefined);
    try {
      // Send the reviewed revision so a stale approval is rejected, not imported.
      const attestation = reviewAttestationHeader(selected);
      await atlasApi(`/api/atlas/paperless/drafts/${selected.paperlessId}/import`, "POST", { expectedRevision: selected.draftRevision }, attestation ? { "X-Atlas-Review-Attestation": attestation } : undefined);
      setMessage("Imported to Receipts. Firefly remains unchanged.");
      await refresh();
      const state = await atlasApi<DraftState>(`/api/atlas/paperless/drafts/${selected.paperlessId}`);
      setSelected(state);
    } catch (e) { setError(e instanceof Error ? e.message : "Import failed"); }
    finally { setBusy(false); }
  }
  async function checkMatch() {
    if (!selected || dirty || !sourceAccountId.trim()) return;
    setBusy(true); setError(undefined); setMatch(null);
    try {
      setMatch(await atlasApi<MatchResult>(
        `/api/atlas/paperless/drafts/${selected.paperlessId}/match`,
        "POST", { fireflySourceAccountId: sourceAccountId.trim() },
      ));
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to check payment"); }
    finally { setBusy(false); }
  }
  async function confirmLink() {
    if (!selected || dirty || !match || match.status !== "PROPOSED_MATCH" || !match.fireflyTransactionId || !sourceAccountId.trim()) return;
    if (!window.confirm(`Link this receipt to Firefly transaction #${match.fireflyTransactionId}? No Firefly data will be changed.`)) return;
    setBusy(true); setError(undefined);
    try {
      const result = await atlasApi<{ status: string }>(
        `/api/atlas/paperless/drafts/${selected.paperlessId}/confirm-match`,
        "POST", { fireflyTransactionId: match.fireflyTransactionId, fireflySourceAccountId: sourceAccountId.trim() },
      );
      setMessage(result.status === "MATCH_CONFIRMED"
        ? `Payment link confirmed to Firefly transaction #${match.fireflyTransactionId}. Firefly remains unchanged.`
        : "Payment link recorded.");
      const state = await atlasApi<DraftState>(`/api/atlas/paperless/drafts/${selected.paperlessId}`);
      setSelected(state);
      await refresh();
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to confirm link"); }
    finally { setBusy(false); }
  }
  return (
    <>
      <PageHead title="Paperless Review" sub="Source documents remain in Paperless; this screen reviews itemized drafts." />
      <div className="mb-4 flex gap-3">
        <button type="button" className="btn xs" onClick={() => void refresh()} disabled={loading || busy}>Refresh inbox</button>
        <Link className="btn xs ghost" to="/receipts">View receipts</Link>
      </div>
      {loading && <p role="status">Loading integration status…</p>}
      {!loading && !configured && !error && (
        <div className="rounded border p-4" role="status">
          <strong>Not connected yet.</strong>
          <p className="mt-2 text-sm">The private Atlas bridge and its server-side operator key must be configured before Paperless drafts can appear here. No automatic processing is enabled.</p>
        </div>
      )}
      {error && <p role="alert" className="mb-4 text-sm text-red-600">{error}</p>}
      {message && <p role="status" className="mb-4 text-sm">{message}</p>}
      {configured && (
        <div className="grid grid-cols-1 gap-5 lg:grid-cols-[minmax(230px,1fr)_minmax(0,2fr)]">
          <section className="rounded border p-3" aria-label="Paperless receipt drafts">
            <h2 className="mb-3 font-semibold">Paperless Inbox</h2>
            {rows.length === 0 && <p className="text-sm text-muted-foreground">No tagged receipt drafts yet.</p>}
            <ul className="space-y-2">
              {rows.map(row => (
                <li key={row.paperlessId}>
                  <button
                    type="button" disabled={busy}
                    className="w-full rounded border px-3 py-2 text-left hover:bg-muted"
                    aria-pressed={selected?.paperlessId === row.paperlessId}
                    onClick={() => void select(row.paperlessId)}
                  >
                    <span className="block font-medium">{row.merchant ?? `Paperless #${row.paperlessId}`}</span>
                    <span className="block text-xs text-muted-foreground">
                      #{row.paperlessId} · {row.date || "Date unknown"} · {row.total || "Total unknown"}
                    </span>
                    <span className="block text-xs">{row.status.replaceAll("_", " ")}</span>
                    {row.sourceStatus && (
                      <span className="block text-xs text-muted-foreground">
                        Original: {row.sourceStatus.status === "AVAILABLE" ? "available in Paperless"
                          : row.sourceStatus.status === "RETIRED" ? "retired from Paperless — purchase record preserved"
                          : row.sourceStatus.status === "DENIED" ? "availability unknown (access denied)"
                          : "availability unknown"}
                      </span>
                    )}
                  </button>
                </li>
              ))}
            </ul>
          </section>
          <section className="rounded border p-4" aria-label="Review selected receipt">
            {!selected || !draft ? (
              <p className="text-sm text-muted-foreground">Select a document to review its extracted purchases.</p>
            ) : (
              <div className="space-y-4">
                <div>
                  <h2 className="font-semibold">Paperless document #{selected.paperlessId}</h2>
                  {paperlessBrowserUrl && (
                    <a href={`${paperlessBrowserUrl}/documents/${selected.paperlessId}/details`} target="_blank" rel="noopener noreferrer" className="text-sm underline">
                      Open original in Paperless
                    </a>
                  )}
                  <p className="text-xs text-muted-foreground">Status: {selected.status} · Draft revision {selected.draftRevision ?? "—"} · Originals remain in Paperless</p>
                  {selected.receiptId && (
                    <Link to={`/receipts/${selected.receiptId}`} className="text-sm underline">Open imported receipt</Link>
                  )}
                  {selected.confirmedFireflyTransactionId && (
                    <p className="text-xs text-muted-foreground">Confirmed payment: Firefly transaction #{selected.confirmedFireflyTransactionId}</p>
                  )}
                </div>
                {selected.problems?.length > 0 && (
                  <div className="rounded border p-3" role="alert">
                    <strong>Needs review</strong>
                    <ul className="list-disc pl-5 text-sm">{selected.problems.map((p, i) => <li key={i}>{p}</li>)}</ul>
                  </div>
                )}
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                  <Field label="Merchant" value={draft.merchant} onChange={merchant => edit({ merchant })} />
                  <Field label="Date (YYYY-MM-DD)" value={draft.date} onChange={date => edit({ date })} />
                  <Field label="Total (USD)" value={draft.total} onChange={total => edit({ total })} />
                </div>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <Field label="Sales tax" value={draft.tax} onChange={tax => edit({ tax })} />
                  <div className="text-sm">Model confidence: <strong>{draft.confidence ?? "Unknown"}</strong></div>
                </div>
                <div className="overflow-x-auto">
                  <h3 className="mb-2 font-medium">Purchased items</h3>
                  <table className="w-full text-sm">
                    <thead><tr><th className="text-left">Product</th><th>Quantity</th><th>Unit</th><th>Unit price</th><th>Line total</th><th><span className="sr-only">Actions</span></th></tr></thead>
                    <tbody>
                      {(draft.items ?? []).map((item, index) => (
                        <Fragment key={index}>
                        <tr>
                          {(["description", "quantity", "unit", "unitPrice", "total"] as const).map(key => (
                            <td key={key} className="p-1">
                              <input
                                aria-label={`Item ${index + 1} ${key}`}
                                className="w-full min-w-20 rounded border bg-background px-2 py-1"
                                value={item[key] ?? ""}
                                onChange={e => editItem(index, key, e.target.value)}
                              />
                            </td>
                          ))}
                          <td className="p-1">
                            <button type="button" className="btn xs ghost" aria-label={`Remove item ${index + 1}`} onClick={() => removeItem(index)}>Remove</button>
                          </td>
                        </tr>
                        <tr>
                          <td colSpan={6} className="p-1">
                            <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
                              <label className="block text-xs">
                                <span className="font-medium">Printed line evidence (exact receipt text)</span>
                                <input
                                  aria-label={`Item ${index + 1} evidence`}
                                  className="mt-1 w-full rounded border bg-background px-2 py-1"
                                  value={item.evidence ?? ""}
                                  onChange={e => editItem(index, "evidence", e.target.value)}
                                  placeholder="Copy the exact printed line"
                                />
                              </label>
                              <label className="block text-xs">
                                <span className="font-medium">Package content qty</span>
                                <input
                                  aria-label={`Item ${index + 1} package content quantity`}
                                  className="mt-1 w-full rounded border bg-background px-2 py-1"
                                  value={item.packageContentQuantity ?? ""}
                                  onChange={e => editItem(index, "packageContentQuantity", e.target.value)}
                                  placeholder="e.g. 64 for a 64 fl oz carton"
                                />
                              </label>
                              <label className="block text-xs">
                                <span className="font-medium">Package content unit</span>
                                <input
                                  aria-label={`Item ${index + 1} package content unit`}
                                  className="mt-1 w-full rounded border bg-background px-2 py-1"
                                  value={item.packageContentUnit ?? ""}
                                  onChange={e => editItem(index, "packageContentUnit", e.target.value)}
                                  placeholder="e.g. fl_oz"
                                />
                              </label>
                            </div>
                          </td>
                        </tr>
                        </Fragment>
                      ))}
                    </tbody>
                  </table>
                  <button type="button" className="btn xs mt-2" onClick={addItem}>Add item</button>
                </div>
                <h3 className="font-medium">Adjustments</h3>
                {(draft.adjustments ?? []).map((adjustment, index) => (
                  <div key={index} className="grid grid-cols-[1fr_1fr_1fr_auto] items-end gap-2">
                    <Field label={`Adjustment ${index + 1} type`} value={adjustment.type} onChange={type => editAdjustment(index, "type", type)} />
                    <Field label={`Adjustment ${index + 1} amount`} value={adjustment.amount} onChange={amount => editAdjustment(index, "amount", amount)} />
                    <Field label={`Adjustment ${index + 1} note`} value={adjustment.description} onChange={description => editAdjustment(index, "description", description)} />
                    <button type="button" className="btn xs ghost" aria-label={`Remove adjustment ${index + 1}`} onClick={() => removeAdjustment(index)}>Remove</button>
                  </div>
                ))}
                <button type="button" className="btn xs" onClick={addAdjustment}>Add adjustment</button>
                <div className="flex flex-wrap gap-2">
                  <button type="button" className="btn" disabled={busy || !dirty} onClick={() => void save()}>Save corrections</button>
                  <button
                    type="button" className="btn primary" disabled={busy || dirty || !!selected.receiptId}
                    onClick={() => void importReceipt()}
                  >Approve &amp; import to Receipts</button>
                </div>
                <div className="border-t pt-4">
                  <h3 className="mb-2 font-medium">Firefly payment check · read only</h3>
                  <p className="mb-2 text-xs text-muted-foreground">Enter an explicitly verified Firefly source-account ID. No transaction is changed or created.</p>
                  <div className="flex gap-2">
                    <input
                      className="min-w-0 flex-1 rounded border bg-background px-3 py-2"
                      aria-label="Firefly source account ID"
                      value={sourceAccountId}
                      onChange={e => setSourceAccountId(e.target.value)}
                    />
                    <button type="button" className="btn" disabled={busy || dirty || !sourceAccountId.trim()} onClick={() => void checkMatch()}>Find payment</button>
                  </div>
                  {match && (
                    <p role="status" className="mt-2 text-sm">
                      {matchLabel(match)}
                      {match.fireflyTransactionId ? ` · Firefly transaction #${match.fireflyTransactionId}` : ""}
                      {match.reason ? ` · ${match.reason}` : ""}
                    </p>
                  )}
                  {match?.status === "PROPOSED_MATCH" && match.fireflyTransactionId && (
                    <button type="button" className="btn mt-2" disabled={busy || dirty} onClick={() => void confirmLink()}>Confirm payment link</button>
                  )}
                </div>
              </div>
            )}
          </section>
        </div>
      )}
    </>
  );
}
