import { useState } from "react";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { localErrorPolicy } from "@/lib/request-error-policy";
import { listCachedTillerTransactions } from "@/lib/tiller-api";

const pageSize = 10;
const money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });

export function TillerTransactionFeed() {
  const [offset, setOffset] = useState(0);
  const [unlinkedOnly, setUnlinkedOnly] = useState(false);
  const query = useQuery({
    ...localErrorPolicy.query,
    queryKey: ["tiller", "dashboard-transactions", offset, unlinkedOnly],
    queryFn: ({ signal }) => listCachedTillerTransactions(offset, pageSize, unlinkedOnly, signal),
  });

  return (
    <section aria-labelledby="tiller-activity-heading" className="rounded-lg border p-4 mb-5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="tiller-activity-heading" className="text-lg font-semibold">Bank activity · Tiller</h2>
        <Link className="card-action" to="/settings/tiller">Tiller settings</Link>
      </div>
      <p className="text-sm text-muted-foreground mb-3">
        Read-only cached transactions, all dates. Includes transfers, income and charges without receipts.
        A receipt is optional; matching does not create another expense.
      </p>
      <div className="flex flex-wrap gap-3 items-center mb-3">
        <label className="flex gap-2 items-center text-sm">
          <input type="checkbox" checked={unlinkedOnly} onChange={(event) => {
            setUnlinkedOnly(event.target.checked);
            setOffset(0);
          }} />
          Without linked receipt only
        </label>
        {query.data && <span className="text-sm">{query.data.total.toLocaleString("en-US")} {unlinkedOnly ? "unlinked" : "cached"} transactions</span>}
      </div>
      {query.isPending ? <p role="status">Loading bank activity…</p> :
        query.isError ? <div role="alert">Bank activity unavailable. <button className="btn xs" type="button" onClick={() => void query.refetch()}>Retry</button></div> :
        query.data.rows.length === 0 ? <p>No transactions on this page.</p> :
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead><tr className="border-b text-left">
              <th scope="col" className="p-2">Date</th>
              <th scope="col" className="p-2">Description</th>
              <th scope="col" className="p-2">Account</th>
              <th scope="col" className="p-2">Category</th>
              <th scope="col" className="p-2 text-right">Amount</th>
              <th scope="col" className="p-2">Receipt</th>
            </tr></thead>
            <tbody>{query.data.rows.map((row) => (
              <tr key={row.id} className="border-b">
                <td className="p-2 whitespace-nowrap">{row.date}</td>
                <td className="p-2">{row.description}<div className="text-xs text-muted-foreground">{row.status}</div></td>
                <td className="p-2">{row.account}</td>
                <td className="p-2">{row.category || "Uncategorized"}</td>
                <td className="p-2 text-right tabular-nums whitespace-nowrap">{money.format(row.amount)}</td>
                <td className="p-2">{row.linkedReceiptId ?
                  <Link className="card-action" to={`/receipts/${row.linkedReceiptId}`}>Linked receipt</Link> :
                  <span className="text-muted-foreground">Not linked</span>}</td>
              </tr>
            ))}</tbody>
          </table>
        </div>}
      {query.data && <div className="flex flex-wrap items-center justify-end gap-2 mt-3">
        <span className="text-sm">{query.data.rows.length ? offset + 1 : 0}–{offset + query.data.rows.length} of {query.data.total}</span>
        <button className="btn xs" type="button" disabled={offset === 0 || query.isFetching} onClick={() => setOffset((n) => Math.max(0, n - pageSize))}>Previous</button>
        <button className="btn xs" type="button" disabled={offset + pageSize >= query.data.total || query.isFetching} onClick={() => setOffset((n) => n + pageSize)}>Next</button>
      </div>}
      {!query.data && offset > 0 && <button className="btn xs" type="button" onClick={() => setOffset(0)}>First page</button>}
    </section>
  );
}
