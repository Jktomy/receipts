import { useQuery, useQueryClient } from "@tanstack/react-query";
import { localErrorPolicy, toastErrorPolicy } from "@/lib/request-error-policy";
import { useSessionMutation } from "@/hooks/useSessionMutation";
import { toast } from "sonner";
import {
  confirmTillerLink,
  getTillerLinkHistory,
  getTillerMatchSuggestion,
  getTillerPaymentContext,
  unlinkTillerLink,
} from "@/lib/tiller-api";

export function useTillerMatchSuggestion(providerTransactionId: string | null) {
  return useQuery({
    ...localErrorPolicy.query,
    queryKey: ["tiller", "match-suggestion", providerTransactionId],
    enabled: !!providerTransactionId,
    queryFn: ({ signal }) =>
      getTillerMatchSuggestion(providerTransactionId!, signal),
  });
}

export function useTillerLinkHistory(providerTransactionId: string | null) {
  return useQuery({
    ...localErrorPolicy.query,
    queryKey: ["tiller", "link-history", providerTransactionId],
    enabled: !!providerTransactionId,
    queryFn: ({ signal }) =>
      getTillerLinkHistory(providerTransactionId!, signal),
  });
}

export function useTillerPaymentContext(receiptId: string | null) {
  return useQuery({
    ...localErrorPolicy.query,
    queryKey: ["tiller", "payment-context", receiptId],
    enabled: !!receiptId,
    queryFn: ({ signal }) => getTillerPaymentContext(receiptId!, signal),
  });
}

export function useConfirmTillerLink() {
  const queryClient = useQueryClient();
  return useSessionMutation({
    ...toastErrorPolicy.mutation,
    mutationFn: (args: {
      providerTransactionId: string;
      receiptId: string;
      reviewer?: string | null;
    }) => confirmTillerLink(args),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["tiller"] });
      toast.success("Transaction linked");
    },
  });
}

export function useUnlinkTillerLink() {
  const queryClient = useQueryClient();
  return useSessionMutation({
    ...toastErrorPolicy.mutation,
    mutationFn: (args: {
      providerTransactionId: string;
      reviewer?: string | null;
    }) => unlinkTillerLink(args),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["tiller"] });
      toast.success("Transaction unlinked");
    },
  });
}
