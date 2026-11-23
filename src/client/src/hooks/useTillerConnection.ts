import { useQuery } from "@tanstack/react-query";
import { localErrorPolicy } from "@/lib/request-error-policy";
import { getTillerConnectionStatus } from "@/lib/tiller-api";

export function useTillerConnection() {
  return useQuery({
    ...localErrorPolicy.query,
    queryKey: ["tiller", "connection-status"],
    refetchInterval: 30_000,
    queryFn: ({ signal }) => getTillerConnectionStatus(signal),
  });
}
