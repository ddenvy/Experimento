"use client";

import { useEffect, useState } from "react";
import {
  api,
  type NextExperimentConfidence,
  type NextExperimentPlanDto,
} from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ArrowRight, Lightbulb, Loader2, RefreshCw } from "lucide-react";

const CONFIDENCE_STYLE: Record<NextExperimentConfidence, string> = {
  Low: "border-slate-300 text-slate-700 bg-slate-50 dark:bg-slate-900/40",
  Medium: "border-yellow-400 text-yellow-700 bg-yellow-50 dark:bg-yellow-950/30",
  High: "border-green-500 text-green-700 bg-green-50 dark:bg-green-950/30",
};

const KIND_LABEL: Record<string, string> = {
  SimulationLead: "Simulation lead",
  RepeatSuccess: "Reproduce success",
  CloseLoop: "Close the loop",
};

// Куда ведёт действие по рекомендации: лид и воспроизведение — во вкладку симуляций,
// закрытие цикла — во вкладку прогнозов, где записывается лабораторный исход.
function targetTab(kind: string): "predictions" | "simulations" {
  return kind === "CloseLoop" ? "predictions" : "simulations";
}

function ParameterChip({ name, value }: { name: string; value: number }) {
  const suffix = ".proportion";
  const label = name.endsWith(suffix) ? `${name.slice(0, -suffix.length)} ${value}` : `${name} ${value}`;
  return (
    <span className="rounded border bg-muted/40 px-1.5 py-0.5 text-[11px] tabular-nums">{label}</span>
  );
}

/**
 * План следующих экспериментов по формуляции. Рекомендации строятся на непроверенных
 * кандидатах симуляций и записанных лабораторных исходах; уверенность падает, когда
 * модель расходится с лабораторией.
 */
export function NextExperimentTab({
  formulationId,
  onOpen,
}: {
  formulationId: string;
  onOpen: (tab: "predictions" | "simulations", versionId: string) => void;
}) {
  const [plan, setPlan] = useState<NextExperimentPlanDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  // Ручное обновление: счётчик перезапускает загрузку без setState в теле эффекта.
  const [reloadToken, setReloadToken] = useState(0);

  // Подписка на внешнюю систему (API): setState только после await,
  // флаг отмены защищает от гонки при смене формуляции.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const result = await api.getNextExperiments(formulationId);
        if (!cancelled) setPlan(result);
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : "Failed to load the plan");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [formulationId, reloadToken]);

  function refresh() {
    setLoading(true);
    setError(null);
    setReloadToken((token) => token + 1);
  }

  if (loading && plan === null) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted-foreground">
        <Loader2 className="h-4 w-4 animate-spin" /> Building the plan…
      </div>
    );
  }

  if (error) {
    return (
      <div className="rounded-md border border-destructive bg-destructive/10 px-4 py-3 text-sm text-destructive">
        {error}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Lightbulb className="h-4 w-4" /> Next experiments
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          {plan && (
            <div className="grid grid-cols-3 gap-3 text-sm">
              <div>
                <span className="text-muted-foreground">Versions: </span>
                {plan.versionsTotal}
              </div>
              <div>
                <span className="text-muted-foreground">Lab outcomes: </span>
                {plan.outcomesRecorded}
              </div>
              <div>
                <span className="text-muted-foreground">Mean model error: </span>
                {plan.meanCalibrationError === null
                  ? "—"
                  : `${(plan.meanCalibrationError * 100).toFixed(0)}%`}
              </div>
            </div>
          )}
          <Button variant="outline" size="sm" onClick={refresh} disabled={loading}>
            {loading ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
            Refresh
          </Button>
        </CardContent>
      </Card>

      {plan !== null && plan.recommendations.length === 0 && (
        <Card className="border-dashed">
          <CardContent className="py-10 text-center text-sm text-muted-foreground">
            Nothing to plan yet — run a simulation or a prediction on a version first.
          </CardContent>
        </Card>
      )}

      {plan !== null && plan.recommendations.length > 0 && (
        <div className="space-y-3" data-testid="next-experiment-list">
          {plan.recommendations.map((item, index) => (
            <Card key={`${item.kind}-${item.versionId}-${index}`} data-testid="next-experiment-item">
              <CardContent className="space-y-2 py-4">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="flex items-center gap-2 text-sm font-medium">
                    <span className="text-xs text-muted-foreground">#{index + 1}</span>
                    {item.title}
                  </span>
                  <span className="flex items-center gap-1.5">
                    <span className="rounded border px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide text-muted-foreground">
                      {KIND_LABEL[item.kind] ?? item.kind}
                    </span>
                    <span
                      className={`rounded border px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide ${CONFIDENCE_STYLE[item.confidence]}`}
                    >
                      {item.confidence} confidence
                    </span>
                  </span>
                </div>

                <p className="text-xs text-muted-foreground">{item.rationale}</p>

                {item.suggestedParameters.length > 0 && (
                  <div className="flex flex-wrap gap-1.5">
                    {item.suggestedParameters.map((p) => (
                      <ParameterChip key={p.name} name={p.name} value={p.value} />
                    ))}
                  </div>
                )}

                <ul className="space-y-0.5 text-[11px] text-muted-foreground">
                  {item.evidence.map((line) => (
                    <li key={line}>· {line}</li>
                  ))}
                </ul>

                {item.versionId && (
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => onOpen(targetTab(item.kind), item.versionId!)}
                  >
                    Open v{item.versionNumber}
                    <ArrowRight className="h-3.5 w-3.5" />
                  </Button>
                )}
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
