import { RequestFailure } from "@/components/RequestFailure";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { useTillerPaymentContext } from "@/hooks/useTillerMatching";
import { formatCurrency } from "@/lib/format";

/**
 * Bank-side payment context for one approved receipt. Separate from the
 * captured receipt totals; a missing link is normal, not a failure.
 */
export function TillerPaymentContextCard({ receiptId }: { receiptId: string }) {
  const { data, isPending, isError, refetch, isFetching } =
    useTillerPaymentContext(receiptId);

  if (isPending) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Payment context</CardTitle>
        </CardHeader>
        <CardContent>
          <Skeleton className="h-10 w-full rounded-lg" />
        </CardContent>
      </Card>
    );
  }

  if (isError) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Payment context</CardTitle>
        </CardHeader>
        <CardContent>
          <RequestFailure
            message="Payment context is unavailable."
            retry={() => void refetch()}
            isRetrying={isFetching}
          />
        </CardContent>
      </Card>
    );
  }

  if (!data) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Payment context</CardTitle>
        </CardHeader>
        <CardContent>
          <p className="text-sm text-muted-foreground">
            No linked bank row — normal for unmatched receipts.
          </p>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Payment context</CardTitle>
      </CardHeader>
      <CardContent>
        <dl className="space-y-1 text-sm">
          <div className="flex gap-2">
            <dt className="text-muted-foreground">Paid</dt>
            <dd>{formatCurrency(data.amount)}</dd>
          </div>
          <div className="flex gap-2">
            <dt className="text-muted-foreground">Date</dt>
            <dd>{data.date}</dd>
          </div>
          <div className="flex gap-2">
            <dt className="text-muted-foreground">Account</dt>
            <dd>{data.account}</dd>
          </div>
          {data.category && (
            <div className="flex gap-2">
              <dt className="text-muted-foreground">Category</dt>
              <dd>{data.category}</dd>
            </div>
          )}
          <div className="flex gap-2">
            <dt className="text-muted-foreground">What</dt>
            <dd>{data.description}</dd>
          </div>
        </dl>
      </CardContent>
    </Card>
  );
}
