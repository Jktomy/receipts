# Atlas Receipts — Paperless document evidence + purchasing analytics

**Status: SOURCE APPLIED to a draft GitHub PR; SYNTHETIC TESTS VERIFIED; NOT DEPLOYED.**

This fork preserves the existing Receipts GUI, reports, PostgreSQL model and native
POST /api/receipts/complete endpoint. The Atlas adapter does **not** add a second
OCR engine or a separate general-purpose receipt/financial service. It is a small
Node 22 process (no npm packages) grouped with the Receipts stack.

## One meaning, one owner

- **Paperless-ngx** is the archive of original PDFs/images, latest approved OCR
  text, document ID, versions and tags.
- **Paperless-AI + B50 PaddleOCR** improve OCR. The adapter fetches the stored
  Paperless text rather than recapturing the document.
- **Open Lyric/Oathgate** transforms OCR text into a structured *draft*. It may
  be a cloud model. The cloud-processing gate is OFF until explicitly enabled.
- **Receipts** stores item-level purchase records and serves the purchasing,
  price-history, category and store analytics GUI.
- **Tiller -> the existing Midas writer -> Firefly III** owns actual account
  balances and financial transactions. Receipts only **reads** Firefly data.

Only Paperless keeps originals. Receipts must retain its own line-item database;
that is what makes historical analysis possible. The bridge's protected state
folder holds temporary drafts, Paperless OCR hashes and imported record IDs —
not a second archive or financial ledger.

## Paperless-native webhook contract

Use a **dedicated** manually qualified Paperless tag such as receipt-analysis.
Do not automatically reassign the existing receipt-wrangler tag or alter the
other HealthLog/TREK workflows as a side effect.

A native Paperless **Document Added / Document Updated** workflow can be
restricted by that tag and configured as a JSON webhook:

    POST https://PRIVATE_ATLAS_BRIDGE/hooks/paperless
    X-Atlas-Hook-Key: <unique webhook-only secret>
    Content-Type: application/json

    { "sourceId": "{{doc_id}}" }

The example is a template. Enter the secret in Paperless's private workflow UI,
not in public source or chat. **include_document = false**: send only the ID,
not the PDF. Paperless's original remains in its own archive.

The bridge verifies the token, then reads:

    GET /api/documents/{id}/
    Authorization: Token <Paperless read-only API token>

It rejects documents without the expected tag, with configured blocked
medical/sensitive tags, without latest OCR content, or with oversized text.
It computes a SHA-256 OCR revision hash, so a change after import is held for
human review rather than creating a second Receipts record.

A **Document Updated** event may occur before Paperless-AI has finished its
improved OCR. A missing/unfinished OCR response is recorded as needing review,
not silently imported. Re-send the same document ID after OCR completion;
the integration must never assume that a tag assignment proves the text is
final. Paperless currently does not guarantee a deletion webhook, so future
orphan reconciliation needs a separate bounded read-only check.

## Structured extraction and arithmetic

A draft includes: merchant, date, grand total, tax, line-item names,
quantities, unit prices, extended prices, product/SKU if printed, adjustments
(coupons/discounts with negative sign), exact OCR evidence snippets, and a
confidence mark. Source evidence is required for merchant, date, total and
every item. Monetary amounts use integer cents; weighted product quantities
and unit prices preserve up to **four decimal places** to match Receipts'
database precision. Model output is *not* treated as authority.

Receipts' total equation must hold exactly to the cent:

    SUM(round(quantity * unitPrice, 2) for each item)
       + tax + signed adjustments = printed receipt total

Unclear values, missing evidence, returns/refunds, mixed tender, line math
errors, incomplete OCR, or low confidence require review. These checks are
necessary but cannot prove that AI parsed a receipt perfectly.

If the owner approves a draft, the adapter calls the existing Receipts API:

    POST /api/receipts/complete
    X-API-Key: <Receipts key scoped to this operator>
    {
      "clientRequestId": "<deterministic UUID from Paperless instance + document ID>",
      "receipt": { "location": "H-E-B", "date": "2026-11-18", "taxAmount": 0.61 },
      "items": [
        { "description": "Milk", "quantity": 1, "unitPrice": 3.99 },
        { "description": "Eggs", "quantity": 1, "unitPrice": 5.00 }
      ],
      "adjustments": [
        { "type": "coupon", "amount": -1.00 }
      ],
      "transactions": []
    }

**All values above are synthetic.** The stable request ID uses Receipts'
native idempotency mechanism, and a changed payload with that ID must cause
a conflict, not a silent second import. No financial transaction is created.

When saved, the itemized purchases appear in the *existing Receipts GUI* and
its store/category/item history. Classification may still need human review.

## Firefly: read-only connector, NOT a ledger plugin

Firefly III exposes a JSON REST API and optional webhooks. Its Data Importer is
for writing financial transactions, which Atlas already does via Midas. Do not
run another import writer just to learn what products were purchased.

The Atlas connector uses **GET /api/v1/transactions**, collects every page
with a bounded page count, verifies response origin, and compares a qualified
receipt against existing withdrawals. It requires the operator to supply
the **explicitly reviewed Firefly funding account ID**; it will not infer it
from OCR or merchant name.

Matching requires exact total, currency, account, merchant and day, with
collision detection across receipts and grouped Firefly transaction splits.
Nearby ambiguous dates/merchants, refunds, mixed-account splits and repeated
source documents remain REVIEW; nothing is automatically linked as authoritative.

Current implementation: returns a PROPOSED_MATCH / REVIEW / UNMATCHED **suggestion**.
It does not write to Firefly. A reviewer can then POST the suggested transaction ID
to confirm-match; the bridge verifies it still equals the current unique suggestion
and stores a durable bridge-side association (confirmedFireflyTransactionId) with
the draft — no Firefly write, no Receipts schema migration. Surfacing a
"View matched transaction" link inside the native Receipts GUI remains a
follow-up source change requiring review.

## Operator endpoints (private, authenticated)

- GET /healthz — no sensitive data, reports whether review mode is enabled
- POST /hooks/paperless — webhook token; asynchronous queue acceptance
- GET /drafts/{paperlessId} — operator token; inspect draft, status, draftRevision
  and any confirmed Firefly association
- PUT /drafts/{paperlessId} — operator token; correct a pending draft (the same
  OCR evidence checks still run before save). Requires { "expectedRevision" }
  naming the revision edited (compare-and-swap): a stale save is rejected with
  409 instead of silently overwriting another reviewer's corrections. Returns
  the new draftRevision and marks the draft reviewer-edited; a later OCR change
  then preserves these corrections instead of silently replacing them. A save
  clears any prior payment confirmation (confirmationInvalidated) because the
  confirmed link described the pre-edit draft.
- POST /drafts/{paperlessId}/import — operator token; reviewed Receipts create.
  Requires { "expectedRevision" }: the approver names the exact draft revision
  reviewed, and a stale approval is rejected with 409 instead of importing a
  newer draft. There is no silent legacy path: an approval without a named
  revision is rejected with 400. Concurrent approvals are serialized; the
  second sees the recorded receiptId (idempotent replay), never a duplicate
  purchase. Saving corrections (PUT) clears any prior payment confirmation and
  reports confirmationInvalidated, and a post-review OCR change driving the
  draft to SOURCE_CHANGED_REVIEW clears it as well — including after an import
  has already recorded the receipt (the imported record stands, but the bridge
  no longer presents the old source as confirmed): the link was checked
  against the old draft or source, so the reviewer must re-check before
  approval. An import attempted after the OCR changed is rejected
  with 409 until the draft is corrected and saved again.
- POST /drafts/{paperlessId}/match — operator token; provide a reviewed
  fireflySourceAccountId, return read-only Firefly candidate IDs.
  A source change detected here blocks the match and voids any stored
  confirmation, same as the import path.
- POST /drafts/{paperlessId}/confirm-match — operator token; provide the
  suggested fireflyTransactionId plus fireflySourceAccountId. Stored only when
  it still equals the current unique PROPOSED_MATCH; rejected with 409
  otherwise. A payment already confirmed on a different receipt is rejected
  with 409 (global one-to-one: checked across all drafts, atomically, so two
  receipts can never claim the same payment); replaying the same receipt's own
  confirmation stays idempotent 200. The draft revision and source hash are
  snapshotted before the Firefly check and re-verified after it responds: a
  reviewer save or OCR change landing mid-flight returns 409 instead of
  confirming a stale draft (no write occurs between re-read and store, so the
  final step is atomic). Never writes to Firefly.
  The confirmed association lives in bridge-side state (durable JSON) and is
  carried into Receipts alongside the new sourcePaperlessDocumentId as soon as
  the import runs — it is not yet a native Receipts field. Native migration
  would require a schema change plus backfill review and is explicitly out of
  this scope; treat bridge-side links as the interim record, verified by the
  synthetic confirm-match tests, not as a ledger entry.

Malformed JSON request bodies fail with 400 (not a generic 422); oversized
bodies fail with 413; semantic validation failures keep 422 with a named
problem. No tokens, OCR text, model output or payment data appear in errors.

Paperless webhook uses X-Atlas-Hook-Key. Operator endpoints use
X-Atlas-Operator-Key. These are separate high-entropy secrets.
Do not expose the operator API on public ingress. The draft API is also exposed through a **native Admin-only Receipts
Paperless Inbox** at /admin/paperless-inbox on the current review branch.
The React page uses the normal Receipts bearer-token session and a same-origin
server-side proxy. Only the Receipts .NET API holds the operator secret file;
the browser never receives it. Its review queue is bounded to 100 recent
Paperless IDs. Optional Paperless source links use a separately configured
HTTPS browser URL. This GUI is source-applied, NOT production deployed.

## Build and tests

Run on Node 22+ without additional dependencies:

    node --test tools/atlas/receipt-match.test.mjs tools/atlas/receipt-bridge.test.mjs

A separately tested Docker image can be built from the fork's repository root
using tools/atlas/Dockerfile. An **opt-in** Compose override is provided at
tools/atlas/docker-compose.atlas.yml. It is disabled by the atlas-review
profile until explicitly enabled; do not promote directly to production.

Compose config requires operator-supplied PRIVATE files for:
Paperless read-only token, Open Lyric token, Receipts API key, webhook secret,
operator secret, and Firefly token. Do not commit these files or credentials.
Use the existing authenticated private secrets store. The container drops
privileges, mounts credentials read-only and binds the published port to
127.0.0.1 by default. Configure cross-container/Tailscale reachability and
firewall rules for the exact deployed network; localhost on another host
does not reach this container.

Set ATLAS_ALLOW_CLOUD_RECEIPT_PROCESSING=1 **only** when sending receipt OCR to
Oathgate's configured route is authorized and appropriate for that document.
Set ATLAS_AUTO_IMPORT_ENABLED=1 only after validated source-specific acceptance.
Both default OFF. Keep medical and other protected tags excluded.

The base Receipts Compose still defaults to an upstream image. Changes to the
forked .NET app or docker-entrypoint.sh do not affect that image until a
separately built, tested and pinned fork image is deployed.

## Operational gates and known limits

- **VERIFIED (synthetic):** source tag filters, OCR evidence, exact receipt
  totals, weighed products, webhook auth, reviewed create, opt-in create,
  duplicate request protection, source revision mismatch and Firefly GET-only
  candidate evaluation. Plus: malformed JSON fails 400 (not generic 422),
  reviewer draft revisions reject stale approvals, concurrent approvals cannot
  duplicate, OCR changes preserve reviewer corrections until re-review, and
  confirm-match stores a durable bridge-side payment association with zero
  Firefly writes. Plus: OCR/model unit spellings normalize to the canonical
  catalog (each/count/mass/volume), unknown units block import for review, and
  the import payload carries unitOfMeasure plus sourcePaperlessDocumentId.
- **NOT VERIFIED:** real Paperless webhook delivery to this bridge,
  live Open Lyric extraction accuracy, live Receipts database persistence and
  restores, production credentials, native GUI end-to-end behavior, and
  persistent Firefly source links. The separate Atlas Docker bridge smoke test
  and full .NET/React suite must pass on the exact review branch before merge.
- **No protected data** was read or stored during the implementation/testing.
- Native Paperless and Midas workflows, current Receipt Wrangler and the
  production Receipts deployment remain unchanged.

### Bridge state backup and restore (isolated test stacks only)

The bridge keeps one JSON file per Paperless document in its data directory
(ATLAS_BRIDGE_DATA_DIR, default ./atlas-receipts-data). To back up: stop only
the bridge container/service, copy the whole data directory to versioned
storage, record its file list with SHA-256 hashes. To restore: first re-verify
every hash against the manifest and refuse the restore on any mismatch (a
tampered archive must never reach pg_restore or the data directory); only then
stop the bridge, replace the data directory with the verified copy, re-check
hashes, restart, then GET /drafts and one known /drafts/{id} and compare
status/draftRevision/receiptId/confirmedFireflyTransactionId against the
recorded values. Never copy production PostgreSQL, Paperless originals or
secret files as part of this procedure; secrets live only in mounted files.

### Rollback

No live changes were made. Abandon this draft branch/PR or revert its commits.
If a future isolated test stack runs, stop only its opt-in bridge service and
preserve its protected draft-state volume for review; do not remove the live
PostgreSQL/secrets volumes or alter Midas/Firefly.
