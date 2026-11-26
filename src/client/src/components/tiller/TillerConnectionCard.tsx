import { RequestFailure } from "@/components/RequestFailure";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { useTillerConnection } from "@/hooks/useTillerConnection";

function formatSyncTime(iso: string | null): string {
  if (!iso) return "never";
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString();
}

export function TillerConnectionCard() {
  const { data, isPending, isError, refetch, isFetching } = useTillerConnection();

  if (isPending) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Tiller connection</CardTitle>
        </CardHeader>
        <CardContent>
          <Skeleton className="h-10 w-full rounded-lg" />
        </CardContent>
      </Card>
    );
  }

  if (isError || !data) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Tiller connection</CardTitle>
        </CardHeader>
        <CardContent>
          <RequestFailure
            message="Tiller status is unavailable. Your receipts are unaffected."
            retry={() => void refetch()}
            isRetrying={isFetching}
          />
        </CardContent>
      </Card>
    );
  }

  if (!data.isConfigured) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Tiller connection</CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          <p className="text-sm text-muted-foreground">
            Tiller is not connected. Connection is configured on the server;
            nothing secret is entered here.
          </p>
          <Badge variant="secondary">Not connected</Badge>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Tiller connection</CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={data.isConnected ? "secondary" : "destructive"}>
            {data.executorManaged
              ? (data.isConnected ? "Hourly sync current" : "Waiting for recent sync")
              : (data.isConnected ? "Connected" : "Connection failed")}
          </Badge>
          {!data.isConnected && (
            <Button
              type="button"
              variant="outline"
              disabled={isFetching}
              onClick={() => void refetch()}
            >
              {isFetching ? "Retrying…" : "Retry"}
            </Button>
          )}
        </div>
        {data.executorManaged && <p className="text-sm text-muted-foreground">Managed by Atlas hourly read-only synchronization. Status reflects the last delivered snapshot.</p>}
        <p className="text-sm text-muted-foreground">
          Spreadsheet: {data.selectedSpreadsheetId ?? "none selected"} · Last
          sync: {formatSyncTime(data.lastSuccessfulSyncUtc)}
        </p>
      </CardContent>
    </Card>
  );
}
