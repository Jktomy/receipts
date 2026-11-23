import { useQuery, useQueryClient } from "@tanstack/react-query";
import { localErrorPolicy, toastErrorPolicy } from "@/lib/request-error-policy";
import { useSessionMutation } from "@/hooks/useSessionMutation";
import { toast } from "sonner";
import {
  createTillerAccountMapping,
  deleteTillerAccountMapping,
  listTillerAccountMappings,
  listTillerAccounts,
  updateTillerAccountMapping,
} from "@/lib/tiller-api";
import type {
  TillerCreateAccountMapping,
  TillerUpdateAccountMapping,
} from "@/lib/tiller-contracts";

export function useTillerAccountMappings() {
  return useQuery({
    ...localErrorPolicy.query,
    queryKey: ["tiller", "account-mappings"],
    queryFn: ({ signal }) => listTillerAccountMappings(signal),
  });
}

export function useTillerAccounts() {
  return useQuery({
    ...localErrorPolicy.query,
    queryKey: ["tiller", "accounts"],
    queryFn: ({ signal }) => listTillerAccounts(signal),
  });
}

function invalidateTiller(queryClient: { invalidateQueries: (filters: { queryKey: string[] }) => unknown }) {
  void queryClient.invalidateQueries({ queryKey: ["tiller"] });
}

export function useCreateTillerAccountMapping() {
  const queryClient = useQueryClient();
  return useSessionMutation({
    ...toastErrorPolicy.mutation,
    mutationFn: (mapping: TillerCreateAccountMapping) =>
      createTillerAccountMapping(mapping),
    onSuccess: () => {
      invalidateTiller(queryClient);
      toast.success("Tiller account mapped");
    },
  });
}

export function useUpdateTillerAccountMapping() {
  const queryClient = useQueryClient();
  return useSessionMutation({
    ...toastErrorPolicy.mutation,
    mutationFn: (mapping: TillerUpdateAccountMapping) =>
      updateTillerAccountMapping(mapping),
    onSuccess: () => {
      invalidateTiller(queryClient);
      toast.success("Tiller account mapping updated");
    },
  });
}

export function useDeleteTillerAccountMapping() {
  const queryClient = useQueryClient();
  return useSessionMutation({
    ...toastErrorPolicy.mutation,
    mutationFn: (id: string) => deleteTillerAccountMapping(id),
    onSuccess: () => {
      invalidateTiller(queryClient);
      toast.success("Tiller account mapping removed");
    },
  });
}
