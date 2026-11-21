import { useEffect } from "react";
import { formatPageTitle } from "@/lib/branding";

export function usePageTitle(title: string) {
  useEffect(() => {
    document.title = formatPageTitle(title);
  }, [title]);
}
