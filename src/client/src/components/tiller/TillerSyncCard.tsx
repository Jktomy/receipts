import { RequestFailure } from "@/components/RequestFailure";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  useSyncTiller,
  useTillerSyncEvents,
  useTillerSyncStatus,
} from "@/hooks/useTillerSync";

export function TillerSyncCard({ executorManaged = false }: { executorManaged?: boolean } = {}) {
  const statusQuery = useTillerSyncStatus();
  const eventsQuery = useTillerSyncEvents(0, 10);
  const syncMutation = useSyncTiller();

  if (statusQuery.isPending) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Tiller sync</CardTitle>
        </CardHeader>
        <CardContent>
          <Skeleton className="h-10 w-full rounded-lg" />
        </CardContent>
      </Card>
    );
  }

  if (statusQuery.isError || !statusQuery.data) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Tiller sync</CardTitle>
        </CardHeader>
        <CardContent>
          <RequestFailure
            message="Sync status is unavailable."
            retry={() => void statusQuery.refetch()}
            isRetrying={statusQuery.isFetching}
          />
        </CardContent>
      </Card>
    );
  }

  const status = statusQuery.data;
  const events = eventsQuery.data?.rows ?? [];

  return (
    <Card>
      <CardHeader>
        <CardTitle>Tiller sync</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={status.completed ? "secondary" : "destructive"}>
            {status.completed ? "Up to date" : "Incomplete"}
          </Badge>
          <span className="text-sm text-muted-foreground">
            Through {status.syncedThroughDate ?? "—"} · Last success{" "}
            {status.lastSuccessfulSyncUtc
              ? new Date(status.lastSuccessfulSyncUtc).toLocaleString()
              : "never"}
          </span>
        </div>
        {executorManaged ? <p className="text-sm text-muted-foreground">Atlas refreshes this snapshot hourly.</p> : <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            variant="outline"
            disabled={syncMutation.isPending}
            onClick={() => syncMutation.mutate("incremental")}
          >
            {syncMutation.isPending ? "Syncing…" : "Sync new rows"}
          </Button>
          <Button
            type="button"
            variant="outline"
            disabled={syncMutation.isPending}
            onClick={() => syncMutation.mutate("full")}
          >
            {syncMutation.isPending ? "Syncing…" : "Full re-sync"}
          </Button>
        </div>}
        {eventsQuery.isError ? (
          <RequestFailure
            message="Sync history is unavailable."
            retry={() => void eventsQuery.refetch()}
            isRetrying={eventsQuery.isFetching}
          />
        ) : events.length > 0 ? (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>When</TableHead>
                <TableHead>Type</TableHead>
                <TableHead className="text-right">Added</TableHead>
                <TableHead className="text-right">Updated</TableHead>
                <TableHead>Status</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {events.map((event) => (
                <TableRow key={event.id}>
                  <TableCell>
                    {new Date(event.occurredAt).toLocaleString()}
                  </TableCell>
                  <TableCell>{event.eventType}</TableCell>
                  <TableCell className="text-right">{event.inserted}</TableCell>
                  <TableCell className="text-right">{event.updated}</TableCell>
                  <TableCell>
                    {event.success ? (
                      "Done"
                    ) : (
                      <span title={event.errorMessage ?? undefined}>
                        Failed
                      </span>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          !eventsQuery.isPending && (
            <p className="text-sm text-muted-foreground">
              No sync runs recorded yet.
            </p>
          )
        )}
      </CardContent>
    </Card>
  );
}
