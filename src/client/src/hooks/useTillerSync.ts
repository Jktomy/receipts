import { useQuery, useQueryClient } from "@tanstack/react-query";
import { localErrorPolicy, toastErrorPolicy } from "@/lib/request-error-policy";
import { useSessionMutation } from "@/hooks/useSessionMutation";
import { toast } from "sonner";
import {
  getTillerSyncStatus,
  listTillerSyncEvents,
  syncTiller,
} from "@/lib/tiller-api";

export function useTillerSyncStatus() {
  return useQuery({
    ...localErrorPolicy.query,
    queryKey: ["tiller", "sync-status"],
    refetchInterval: 30_000,
    queryFn: ({ signal }) => getTillerSyncStatus(signal),
  });
}

export function useTillerSyncEvents(offset = 0, limit = 25, outcome?: "success" | "failure") {
  return useQuery({
    ...localErrorPolicy.query,
    queryKey: ["tiller", "sync-events", offset, limit, outcome ?? "all"],
    queryFn: ({ signal }) => listTillerSyncEvents(offset, limit, outcome, signal),
  });
}

export function useSyncTiller() {
  const queryClient = useQueryClient();
  return useSessionMutation({
    ...toastErrorPolicy.mutation,
    mutationFn: (mode: "full" | "incremental") => syncTiller(mode),
    onSuccess: (result) => {
      void queryClient.invalidateQueries({ queryKey: ["tiller"] });
      toast.success(
        `Tiller sync complete: ${result.inserted} added, ${result.updated} updated`,
      );
    },
  });
}
