# Atlas Receipts — Firefly read-only integration preflight

**Stage: branch source only. NOT DEPLOYED.** No personal documents, receipts, financial records, tokens or accounts were accessed.

## Responsibility boundaries

- Paperless-ngx keeps the single original receipt and document ID/hash.
- PaddleOCR v6 and Open Lyric via Oathgate propose line items; a reviewer qualifies them.
- Receipts owns item-level purchase history, price and spending analysis.
- Tiller → Midas (sole writer) → Firefly III owns actual financial transactions.
- This utility NEVER writes, splits, creates or deletes Firefly transactions.
- Optional financial category splits require a separate approved Midas change.

## Offline matcher

Node 20+; no third-party packages. Test and run:

    node --test tools/atlas/receipt-match.test.mjs
    node tools/atlas/receipt-match.mjs PRIVATE_RECEIPTS.json PRIVATE_FIREFLY.json > PRIVATE_MATCHES.json

The utility makes **no network calls** and writes only its result to stdout.
Inputs and output are sensitive household financial data: keep them in protected
storage, **never** in this public GitHub repository.

First input is an array of reviewed, normalized receipt totals (not raw OCR).
Amounts must be positive decimal strings, with the full total including tax
and adjustments. Example using entirely synthetic data:

    [
      {
        "receiptId": "synthetic-r1",
        "paperlessDocumentId": "synthetic-doc1",
        "merchant": "H-E-B",
        "date": "2026-11-18",
        "amount": "42.75",
        "currency": "USD",
        "fireflySourceAccountId": "7"
      }
    ]

The Firefly funding source account ID must come from an explicit reviewed
Receipts-card-to-Firefly-account mapping; **never infer it from the receipt**.

Second input: a complete GET /api/v1/transactions Firefly JSON response. Example:

    {
      "data": [
        {"id": "31", "attributes": {"transactions": [
          {
            "type": "withdrawal",
            "date": "2026-11-18T08:00:00-06:00",
            "amount": "42.75",
            "currency_code": "USD",
            "source_id": "7",
            "destination_name": "HEB"
          }
        ]}}
      ],
      "links": {"next": null},
      "meta": {"pagination": {"current_page": 1, "total_pages": 1}}
    }

This prototype does not fetch Firefly data. A future authorized exporter must
gather and verify ALL API pages before passing a combined snapshot. Incomplete
pagination causes a hard error; missing transaction coverage is not evidence of
no expense.

Statuses: PROPOSED_MATCH only for a unique exact funding account, currency,
merchant, positive amount and calendar date match. It is only a candidate.
REVIEW for ambiguous account/date/merchant matches, repeat sources, negative
refunds and invalid facts. UNMATCHED if the input snapshot has no candidate.
The matcher checks uniqueness across all input receipts and Firefly groups.
Multiple Firefly withdrawal splits may be summed only when their account,
currency, date and merchant are consistent. Transfers/deposits are ignored.

## Deployment notes specific to Atlas

The fork's Compose config defaults to localhost binding and a pinned upstream
image. Use a same-host proxy or explicitly bind to the private Tailscale
interface for another trusted proxy. Do not bind to all interfaces. Timezone
defaults to America/Chicago.

**Critical:** The upstream GHCR image does not contain changes to the fork's
docker-entrypoint.sh, so the upstream bootstrap password can still appear in
container logs. A reviewed build of this fork is needed for that change.
Configuring Compose by itself is not proof that source changes have shipped.

The Compose init restricts JWT and initial administrator secret file
permissions, while keeping the database password readable by PostgreSQL's
separate container user. Do not delete the secrets volume from an existing
installation to rotate a database password. Protect DB and secrets together.

## Not yet implemented / acceptance gates

A live Firefly read-only connector, link persistence in Receipts, direct
Paperless intake, weight/package-size normalization and production automation
are all OUT OF SCOPE for this prototype. Do not claim these are deployed.

Before production: independently build and test the fork image; verify login,
container private ingress, secret handling, audited receipt math, synthetic
refunds/discounts/weighted products, duplicate and split-match handling,
source-to-Firefly links, and isolated backup/restore. All ledger writes remain
with Midas and require separate user authorization.

Rollback: this branch has no effect on running services or main. Abandon it if
testing or review fails.
