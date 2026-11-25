import { useState } from "react";
import { RequestFailure } from "@/components/RequestFailure";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import {
  useSelectedTillerSpreadsheet,
  useSelectTillerSpreadsheet,
  useTillerSpreadsheets,
} from "@/hooks/useTillerSpreadsheets";

export function TillerSpreadsheetCard({ executorManaged = false }: { executorManaged?: boolean } = {}) {
  const spreadsheetsQuery = useTillerSpreadsheets();
  const selectedQuery = useSelectedTillerSpreadsheet();
  const selectMutation = useSelectTillerSpreadsheet();
  const [choice, setChoice] = useState<string | null>(null);

  const pending = spreadsheetsQuery.isPending || selectedQuery.isPending;
  if (pending) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Tiller spreadsheet</CardTitle>
        </CardHeader>
        <CardContent>
          <Skeleton className="h-10 w-full rounded-lg" />
        </CardContent>
      </Card>
    );
  }

  if (spreadsheetsQuery.isError || selectedQuery.isError) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Tiller spreadsheet</CardTitle>
        </CardHeader>
        <CardContent>
          <RequestFailure
            message="Spreadsheet choices are unavailable."
            retry={() => {
              void spreadsheetsQuery.refetch();
              void selectedQuery.refetch();
            }}
            isRetrying={
              spreadsheetsQuery.isFetching || selectedQuery.isFetching
            }
          />
        </CardContent>
      </Card>
    );
  }

  const spreadsheets = spreadsheetsQuery.data ?? [];
  const selected = choice ?? selectedQuery.data?.selectedSpreadsheetId ?? "";

  return (
    <Card>
      <CardHeader>
        <CardTitle>Tiller spreadsheet</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        {executorManaged ? (
          <p className="text-sm">{spreadsheets.find((sheet) => sheet.spreadsheetId === selected)?.title ?? selected} · Managed by Atlas</p>
        ) : spreadsheets.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No Tiller spreadsheets found. This is normal before the first
            connection.
          </p>
        ) : (
          <div className="flex flex-wrap items-center gap-2">
            <Select value={selected} onValueChange={setChoice}>
              <SelectTrigger aria-label="Tiller spreadsheet">
                <SelectValue placeholder="Choose a spreadsheet" />
              </SelectTrigger>
              <SelectContent>
                {spreadsheets.map((sheet) => (
                  <SelectItem
                    key={sheet.spreadsheetId}
                    value={sheet.spreadsheetId}
                    disabled={!sheet.editable}
                  >
                    {sheet.title}
                    {sheet.editable ? "" : " (Direct Fill off — can't sync)"}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Button
              type="button"
              disabled={!selected || selectMutation.isPending}
              onClick={() => selectMutation.mutate(selected)}
            >
              {selectMutation.isPending ? "Saving…" : "Use spreadsheet"}
            </Button>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
