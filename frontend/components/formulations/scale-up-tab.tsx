"use client";

import { useState } from "react";
import {
  api,
  type FormulationVersionDto,
  type ScaleUpAssessmentDto,
  type ScaleUpSeverity,
} from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Gauge, Loader2 } from "lucide-react";

const SEVERITY_STYLE: Record<ScaleUpSeverity, string> = {
  Low: "border-slate-300 text-slate-700 bg-slate-50 dark:bg-slate-900/40",
  Medium: "border-yellow-400 text-yellow-700 bg-yellow-50 dark:bg-yellow-950/30",
  High: "border-orange-500 text-orange-700 bg-orange-50 dark:bg-orange-950/30",
  Critical: "border-red-500 text-red-700 bg-red-50 dark:bg-red-950/30",
};

// Цвет итогового балла: зелёный — перенос без изменений, красный — требуется переработка.
function scoreColor(score: number): string {
  if (score >= 95) return "text-green-600";
  if (score >= 75) return "text-yellow-600";
  if (score >= 50) return "text-orange-600";
  return "text-red-600";
}

/**
 * Оценка готовности версии к масштабированию на целевой объём партии.
 * Расчёт детерминированный (теплоотвод, газовыделение, растворитель, pH, полнота данных)
 * и выполняется по нажатию кнопки — объём можно перебрать без перезагрузки страницы.
 */
export function ScaleUpTab({
  versions,
  initialVersionId,
}: {
  versions: FormulationVersionDto[];
  initialVersionId?: string;
}) {
  const [versionId, setVersionId] = useState(initialVersionId ?? versions[0]?.id ?? "");
  const [targetVolume, setTargetVolume] = useState(10);
  const [assessment, setAssessment] = useState<ScaleUpAssessmentDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Внешняя передача версии (?version=): корректировка во время рендера, без эффекта.
  const [prevInitialVersionId, setPrevInitialVersionId] = useState(initialVersionId);
  if (initialVersionId && initialVersionId !== prevInitialVersionId) {
    setPrevInitialVersionId(initialVersionId);
    setVersionId(initialVersionId);
  }

  async function assess() {
    if (!versionId) return;
    setLoading(true);
    setError(null);
    try {
      setAssessment(await api.getScaleUpAssessment(versionId, Number(targetVolume)));
    } catch (e) {
      setAssessment(null);
      setError(e instanceof Error ? e.message : "Assessment failed");
    } finally {
      setLoading(false);
    }
  }

  if (versions.length === 0) {
    return (
      <Card className="border-dashed">
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          Create a version on the Composition tab before assessing scale-up.
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle>Scale-up assessment</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-wrap items-end gap-4">
            <div>
              <label className="text-xs text-muted-foreground">Version</label>
              <select
                aria-label="Select version"
                className="block h-10 rounded-md border border-input bg-background px-3 text-sm"
                value={versionId}
                onChange={(e) => setVersionId(e.target.value)}
              >
                {versions.map((v) => (
                  <option key={v.id} value={v.id}>
                    v{v.versionNumber}
                  </option>
                ))}
              </select>
            </div>
            <div className="max-w-[180px]">
              <label className="text-xs text-muted-foreground">Target batch size (L)</label>
              <Input
                type="number"
                aria-label="Target batch size in litres"
                value={targetVolume}
                onChange={(e) => setTargetVolume(Number(e.target.value))}
                min={1}
                max={10000}
              />
            </div>
            <Button onClick={assess} disabled={loading || !versionId} data-testid="assess-scale-up">
              {loading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Gauge className="h-4 w-4" />}
              Assess scale-up
            </Button>
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
        </CardContent>
      </Card>

      {assessment && (
        <Card data-testid="scale-up-result">
          <CardHeader>
            <CardTitle className="text-base">
              v{assessment.versionNumber} at {assessment.targetVolumeLitres} L
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap items-baseline gap-3">
              <span
                className={`text-3xl font-bold tabular-nums ${scoreColor(assessment.readinessScore)}`}
                data-testid="scale-up-score"
              >
                {assessment.readinessScore}
              </span>
              <span className="text-sm text-muted-foreground">
                / 100 · {assessment.verdict}
              </span>
              <span className="text-xs text-muted-foreground">
                {assessment.scaleFactor.toFixed(1)}× bench scale (
                {assessment.labReferenceVolumeLitres} L → {assessment.targetVolumeLitres} L)
              </span>
            </div>

            {assessment.findings.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                No scale-up risks flagged for this volume.
              </p>
            ) : (
              <ul className="space-y-2" data-testid="scale-up-findings">
                {assessment.findings.map((f) => (
                  <li key={f.factor} className="rounded border bg-background p-3">
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-sm font-medium">{f.factor}</span>
                      <span
                        className={`rounded border px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide ${SEVERITY_STYLE[f.severity]}`}
                      >
                        {f.severity}
                      </span>
                    </div>
                    <p className="mt-1 text-xs text-muted-foreground">{f.observation}</p>
                    <p className="mt-1 text-xs">{f.recommendation}</p>
                  </li>
                ))}
              </ul>
            )}

            <p className="text-[11px] text-muted-foreground">
              Screening aid only — confirm the heat balance with reaction calorimetry before the
              pilot batch.
            </p>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
