import { useState } from "react";
import { RequestFailure } from "@/components/RequestFailure";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import {
  useConfirmTillerLink,
  useTillerLinkHistory,
  useTillerMatchSuggestion,
  useUnlinkTillerLink,
} from "@/hooks/useTillerMatching";

/**
 * Optional receipt↔transaction matching for one bank row. Linking is a
 * choice, never a requirement: unmatched rows are normal and stay neutral.
 */
export function TillerMatchCard({
  providerTransactionId,
}: {
  providerTransactionId: string;
}) {
  const suggestionQuery = useTillerMatchSuggestion(providerTransactionId);
  const historyQuery = useTillerLinkHistory(providerTransactionId);
  const confirmMutation = useConfirmTillerLink();
  const unlinkMutation = useUnlinkTillerLink();
  const [reviewer, setReviewer] = useState("");

  if (suggestionQuery.isPending) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Receipt link</CardTitle>
        </CardHeader>
        <CardContent>
          <Skeleton className="h-10 w-full rounded-lg" />
        </CardContent>
      </Card>
    );
  }

  if (suggestionQuery.isError || !suggestionQuery.data) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Receipt link</CardTitle>
        </CardHeader>
        <CardContent>
          <RequestFailure
            message="Link suggestions are unavailable."
            retry={() => void suggestionQuery.refetch()}
            isRetrying={suggestionQuery.isFetching}
          />
        </CardContent>
      </Card>
    );
  }

  const suggestion = suggestionQuery.data;
  const history = historyQuery.data ?? [];
  const busy = confirmMutation.isPending || unlinkMutation.isPending;
  const attribution = reviewer.trim() ? reviewer.trim() : undefined;

  const unlink = (
    <Button
      type="button"
      variant="outline"
      disabled={busy}
      onClick={() =>
        unlinkMutation.mutate({
          providerTransactionId,
          reviewer: attribution,
        })
      }
    >
      {unlinkMutation.isPending ? "Unlinking…" : "Unlink"}
    </Button>
  );

  return (
    <Card>
      <CardHeader>
        <CardTitle>Receipt link</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        {(suggestion.decision === "linked" || suggestion.decision === "manual") &&
        suggestion.receiptId ? (
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm">
              Linked{suggestion.decision === "manual" ? " by hand" : ""}.
            </span>
            {unlink}
          </div>
        ) : suggestion.decision === "ambiguous" &&
          suggestion.candidates.length > 0 ? (
          <div className="space-y-2">
            <p className="text-sm text-muted-foreground">
              {suggestion.candidates.length} possible receipts. Linking is
              optional.
            </p>
            {suggestion.candidates.map((candidate) => (
              <div
                key={candidate.receiptId}
                className="flex flex-wrap items-center gap-2"
              >
                <span className="text-sm font-mono">{candidate.receiptId}</span>
                <Button
                  type="button"
                  variant="outline"
                  disabled={busy}
                  onClick={() =>
                    confirmMutation.mutate({
                      providerTransactionId,
                      receiptId: candidate.receiptId,
                      reviewer: attribution,
                    })
                  }
                >
                  {confirmMutation.isPending ? "Linking…" : "Link"}
                </Button>
              </div>
            ))}
            {unlink}
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">
            Not linked — normal for unmatched rows.
          </p>
        )}
        <div className="flex flex-wrap items-center gap-2">
          <label className="text-sm font-medium" htmlFor="tiller-reviewer">
            Reviewer
          </label>
          <Input
            id="tiller-reviewer"
            className="max-w-[200px]"
            placeholder="Optional name"
            value={reviewer}
            onChange={(e) => setReviewer(e.target.value)}
          />
        </div>
        {history.length > 0 && (
          <ul className="space-y-1 text-sm text-muted-foreground">
            {history.map((entry, index) => (
              <li key={`${entry.at}-${index}`}>
                {entry.action}
                {entry.reviewer ? ` by ${entry.reviewer}` : ""} ·{" "}
                {new Date(entry.at).toLocaleString()}
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
