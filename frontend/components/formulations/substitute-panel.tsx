"use client";

import { useEffect, useState } from "react";
import { api, type SubstituteCandidateDto } from "@/lib/api";
import { STATUS_STYLE } from "@/lib/regulations";
import { Loader2, X, FlaskConical } from "lucide-react";

/**
 * Панель подбора замен для одного компонента формуляции.
 * Кандидаты ранжируются по близости состава, массы и класса опасности;
 * регуляторный статус показывается и учитывается в ранжирующем балле.
 */
export function SubstitutePanel({
  cid,
  componentName,
  onClose,
}: {
  cid: number;
  componentName: string;
  onClose: () => void;
}) {
  const [candidates, setCandidates] = useState<SubstituteCandidateDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Подписка на внешнюю систему (API): setState только после await,
  // флаг отмены защищает от гонки при быстром переключении компонентов.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const items = await api.findSubstitutes(cid);
        if (!cancelled) setCandidates(items);
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : "Failed to load substitutes");
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [cid]);

  return (
    <div className="mt-2 rounded-md border bg-muted/30 p-3" data-testid="substitute-panel">
      <div className="flex items-start justify-between gap-2">
        <div className="text-xs font-medium">
          Substitutes for <span className="text-foreground">{componentName}</span>
        </div>
        <button
          type="button"
          onClick={onClose}
          className="text-muted-foreground hover:text-foreground"
          aria-label="Close substitutes"
        >
          <X className="h-3.5 w-3.5" />
        </button>
      </div>

      {candidates === null && !error && (
        <div className="mt-2 flex items-center gap-2 text-xs text-muted-foreground">
          <Loader2 className="h-3.5 w-3.5 animate-spin" /> Searching the catalog…
        </div>
      )}

      {error && <p className="mt-2 text-xs text-destructive">{error}</p>}

      {candidates !== null && candidates.length === 0 && !error && (
        <p className="mt-2 text-xs text-muted-foreground">
          No suitable substitutes found in the catalog for this substance.
        </p>
      )}

      {candidates !== null && candidates.length > 0 && (
        <ul className="mt-2 space-y-2" data-testid="substitute-list">
          {candidates.map((c) => {
            const style = STATUS_STYLE[c.regulatoryStatus];
            return (
              <li key={c.pubChemCid} className="rounded border bg-background p-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="flex items-center gap-1.5 text-xs font-medium">
                    <FlaskConical className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                    {c.name}
                  </span>
                  <span className="flex items-center gap-1.5">
                    <span className="text-xs tabular-nums text-muted-foreground">
                      {(c.similarity * 100).toFixed(0)}% match
                    </span>
                    {c.regulatoryStatus !== "Compliant" && (
                      <span
                        className={`rounded border px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide ${style.badge}`}
                      >
                        {style.label}
                      </span>
                    )}
                  </span>
                </div>
                <div className="mt-0.5 text-[11px] text-muted-foreground">
                  {c.formula ?? "—"} · {c.molarMass.toFixed(2)} g/mol
                  {c.casNumber ? ` · CAS ${c.casNumber}` : ""} · CID {c.pubChemCid}
                </div>
                <div className="mt-1 text-[11px] text-muted-foreground">{c.matchedSignals.join(" · ")}</div>
              </li>
            );
          })}
        </ul>
      )}

      <p className="mt-2 text-[11px] text-muted-foreground">
        Screening aid only — validate any replacement experimentally in a new version.
      </p>
    </div>
  );
}
