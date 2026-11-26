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
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { useAllAccounts } from "@/hooks/useAccounts";
import {
  useCreateTillerAccountMapping,
  useDeleteTillerAccountMapping,
  useTillerAccountMappings,
  useTillerAccounts,
} from "@/hooks/useTillerAccountMappings";
import { useSelectedTillerSpreadsheet } from "@/hooks/useTillerSpreadsheets";

export function TillerAccountMappingsCard({ executorManaged = false }: { executorManaged?: boolean } = {}) {
  const mappingsQuery = useTillerAccountMappings();
  const tillerAccountsQuery = useTillerAccounts();
  const receiptsAccountsQuery = useAllAccounts();
  const selectedQuery = useSelectedTillerSpreadsheet();
  const createMutation = useCreateTillerAccountMapping();
  const deleteMutation = useDeleteTillerAccountMapping();
  const [receiptsAccountId, setReceiptsAccountId] = useState("");
  const [tillerAccountId, setTillerAccountId] = useState("");

  const pending = mappingsQuery.isPending || tillerAccountsQuery.isPending;
  if (pending) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Tiller account mapping</CardTitle>
        </CardHeader>
        <CardContent>
          <Skeleton className="h-10 w-full rounded-lg" />
        </CardContent>
      </Card>
    );
  }

  if (mappingsQuery.isError || tillerAccountsQuery.isError) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Tiller account mapping</CardTitle>
        </CardHeader>
        <CardContent>
          <RequestFailure
            message="Account mappings are unavailable."
            retry={() => {
              void mappingsQuery.refetch();
              void tillerAccountsQuery.refetch();
            }}
            isRetrying={
              mappingsQuery.isFetching || tillerAccountsQuery.isFetching
            }
          />
        </CardContent>
      </Card>
    );
  }

  const mappings = mappingsQuery.data ?? [];
  const tillerAccounts = tillerAccountsQuery.data ?? [];
  const receiptsAccounts = receiptsAccountsQuery.data ?? [];
  const spreadsheetId = selectedQuery.data?.selectedSpreadsheetId ?? "";
  const chosenTiller = tillerAccounts.find((a) => a.accountId === tillerAccountId);
  const canCreate =
    !!receiptsAccountId &&
    !!tillerAccountId &&
    !!spreadsheetId &&
    !createMutation.isPending;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Tiller account mapping</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        {executorManaged && (
          <div className="space-y-2">
            <p className="text-sm font-medium">{tillerAccounts.length} linked Tiller accounts · Read-only</p>
            <Table>
              <TableHeader><TableRow><TableHead>Account</TableHead><TableHead>Institution</TableHead><TableHead>Type</TableHead></TableRow></TableHeader>
              <TableBody>{tillerAccounts.map((account) => (
                <TableRow key={account.accountId}><TableCell>{account.displayName}</TableCell><TableCell>{account.institution}</TableCell><TableCell>{account.accountType}</TableCell></TableRow>
              ))}</TableBody>
            </Table>
          </div>
        )}
        {mappings.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No accounts mapped yet. Mapping is optional and only affects
            which Tiller rows appear as payment context.
          </p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Receipts account</TableHead>
                <TableHead>Tiller account</TableHead>
                <TableHead className="w-24">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {mappings.map((mapping) => (
                <TableRow key={mapping.id}>
                  <TableCell>{mapping.receiptsAccountId}</TableCell>
                  <TableCell>{mapping.tillerAccountName}</TableCell>
                  <TableCell>
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      disabled={deleteMutation.isPending}
                      onClick={() => deleteMutation.mutate(mapping.id)}
                    >
                      Remove
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
        <div className="flex flex-wrap items-center gap-2">
          <Select value={receiptsAccountId} onValueChange={setReceiptsAccountId}>
            <SelectTrigger aria-label="Receipts account">
              <SelectValue placeholder="Receipts account" />
            </SelectTrigger>
            <SelectContent>
              {receiptsAccounts.map((account: { id: string; name: string }) => (
                <SelectItem key={account.id} value={account.id}>
                  {account.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Select value={tillerAccountId} onValueChange={setTillerAccountId}>
            <SelectTrigger aria-label="Tiller account">
              <SelectValue placeholder="Tiller account" />
            </SelectTrigger>
            <SelectContent>
              {tillerAccounts.map((account) => (
                <SelectItem key={account.accountId} value={account.accountId}>
                  {account.displayName} · {account.institution}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Button
            type="button"
            disabled={!canCreate}
            onClick={() =>
              createMutation.mutate({
                receiptsAccountId,
                tillerAccountId,
                tillerAccountName:
                  chosenTiller?.displayName ?? tillerAccountId,
                spreadsheetId,
              })
            }
          >
            {createMutation.isPending ? "Saving…" : "Map accounts"}
          </Button>
        </div>
        {!spreadsheetId && (
          <p className="text-sm text-muted-foreground">
            Choose a Tiller spreadsheet above before mapping accounts.
          </p>
        )}
      </CardContent>
    </Card>
  );
}
