import { AUTHORITY_LABEL, STATUS_STYLE } from "@/lib/regulations";
import type { ChemicalRegulationSummaryDto } from "@/lib/api";
import { AlertTriangle, Ban, ShieldCheck } from "lucide-react";

interface RegulationBadgeProps {
  summary: ChemicalRegulationSummaryDto;
  showLabel?: boolean;
  size?: "sm" | "xs";
}

/**
 * Компактный бейдж регуляторного статуса вещества.
 * Показывает наивысший (самый строгий) статус одним значком.
 * При наведении — тултип со списком всех органов и причин.
 */
export function RegulationBadge({ summary, showLabel = true, size = "sm" }: RegulationBadgeProps) {
  const style = STATUS_STYLE[summary.highestStatus];
  const hasRegs = summary.regulations.length > 0;

  const Icon = summary.highestStatus === "Banned"
    ? Ban
    : summary.highestStatus === "Restricted"
    ? AlertTriangle
    : ShieldCheck;

  const sizeClass = size === "xs" ? "h-3 w-3" : "h-3.5 w-3.5";

  return (
    <span
      className={`inline-flex items-center gap-1 rounded border px-1.5 py-0.5 font-medium uppercase tracking-wide ${style.badge} ${
        size === "xs" ? "text-[9px]" : "text-[10px]"
      }`}
      title={
        hasRegs
          ? summary.regulations
              .map((r) => `${AUTHORITY_LABEL[r.authority]}: ${r.reason}`)
              .join("\n")
          : "No regulatory flags in our database"
      }
    >
      <Icon className={`${sizeClass} shrink-0`} />
      {showLabel && <span>{style.label}</span>}
    </span>
  );
}
