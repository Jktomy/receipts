import { useQuery, useQueryClient } from "@tanstack/react-query";
import { localErrorPolicy, toastErrorPolicy } from "@/lib/request-error-policy";
import { useSessionMutation } from "@/hooks/useSessionMutation";
import { toast } from "sonner";
import {
  getSelectedTillerSpreadsheet,
  listTillerSpreadsheets,
  selectTillerSpreadsheet,
} from "@/lib/tiller-api";

export function useTillerSpreadsheets() {
  return useQuery({
    ...localErrorPolicy.query,
    queryKey: ["tiller", "spreadsheets"],
    queryFn: ({ signal }) => listTillerSpreadsheets(signal),
  });
}

export function useSelectedTillerSpreadsheet() {
  return useQuery({
    ...localErrorPolicy.query,
    queryKey: ["tiller", "selected-spreadsheet"],
    queryFn: ({ signal }) => getSelectedTillerSpreadsheet(signal),
  });
}

export function useSelectTillerSpreadsheet() {
  const queryClient = useQueryClient();
  return useSessionMutation({
    ...toastErrorPolicy.mutation,
    mutationFn: (spreadsheetId: string) => selectTillerSpreadsheet(spreadsheetId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["tiller"] });
      toast.success("Tiller spreadsheet selected");
    },
  });
}
