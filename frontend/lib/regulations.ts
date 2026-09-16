import type { RegulationStatus, RegulationAuthority } from "@/lib/api";

/** Читаемое имя регулятора для показа в UI. */
export const AUTHORITY_LABEL: Record<RegulationAuthority, string> = {
  ReachSvhc: "REACH SVHC",
  EpaPfas: "EPA PFAS",
  CaliforniaProp65: "CA Prop 65",
  Voc: "VOC",
};

/**
 * Цветовая схема бейджа по статусу.
 * Banned — красный, Restricted — жёлтый/оранжевый, Compliant — зелёный.
 */
export const STATUS_STYLE: Record<
  RegulationStatus,
  { badge: string; text: string; label: string }
> = {
  Banned: {
    badge: "bg-red-100 text-red-800 border-red-200 dark:bg-red-950/40 dark:text-red-300 dark:border-red-900",
    text: "text-red-600 dark:text-red-400",
    label: "Banned",
  },
  Restricted: {
    badge: "bg-amber-100 text-amber-800 border-amber-200 dark:bg-amber-950/40 dark:text-amber-300 dark:border-amber-900",
    text: "text-amber-600 dark:text-amber-400",
    label: "Restricted",
  },
  Compliant: {
    badge: "bg-green-100 text-green-800 border-green-200 dark:bg-green-950/40 dark:text-green-300 dark:border-green-900",
    text: "text-green-600 dark:text-green-400",
    label: "Compliant",
  },
};

/** Порядок отображения — от самого строгого к менее. */
export const STATUS_ORDER: Record<RegulationStatus, number> = {
  Banned: 0,
  Restricted: 1,
  Compliant: 2,
};

export function highestStatus(statuses: RegulationStatus[]): RegulationStatus {
  if (statuses.length === 0) return "Compliant";
  return statuses.reduce((a, b) =>
    STATUS_ORDER[a] <= STATUS_ORDER[b] ? a : b
  );
}
