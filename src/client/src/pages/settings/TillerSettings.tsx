import { PageHead } from "@/components/primitives";
import { TillerAccountMappingsCard } from "@/components/tiller/TillerAccountMappingsCard";
import { TillerConnectionCard } from "@/components/tiller/TillerConnectionCard";
import { TillerSpreadsheetCard } from "@/components/tiller/TillerSpreadsheetCard";
import { TillerSyncCard } from "@/components/tiller/TillerSyncCard";
import { usePageTitle } from "@/hooks/usePageTitle";

/**
 * Native Tiller settings (W26 UI). Route registration stays with W20:
 * mount this page (e.g. at /settings/tiller) once W24 slice 5 publishes
 * its routes. Bank rows never become receipt items here; unmatched rows
 * are normal and carry no nags, scores or required tasks.
 */
export default function TillerSettings() {
  usePageTitle("Tiller Settings");

  return (
    <div className="space-y-6">
      <PageHead title="Tiller Settings" />
      <p className="text-sm text-muted-foreground">
        Read-only bank history for payment context. Receipt matching is
        optional; captured receipt totals and bank spending stay separate.
        Bank rows never become receipt items here; unmatched rows are normal.
      </p>
      <TillerConnectionCard />
      <TillerSpreadsheetCard />
      <TillerAccountMappingsCard />
      <TillerSyncCard />
    </div>
  );
}
