"use client";

import { useEffect, useRef, useState } from "react";
import {
  api,
  type FormulationVersionDto,
  type PredictionResultDto,
  type PredictionRunSummaryDto,
} from "@/lib/api";
import { trackJob, type JobTrackHandle } from "@/lib/track-job";
import { formatDateTime, formatPercent } from "@/lib/format";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Brain, Loader2, ArrowRight, CheckCircle2, History } from "lucide-react";
import { RunStatusBadge } from "@/components/runs/run-status-badge";
import { OutcomePanel } from "@/components/predictions/outcome-panel";

const sideRiskColor: Record<string, string> = {
  Low: "text-green-600",
  Medium: "text-yellow-600",
  High: "text-red-600",
};

export function PredictionsTab({
  versions,
  initialVersionId,
  onContinueToSimulation,
}: {
  versions: FormulationVersionDto[];
  initialVersionId?: string;
  onContinueToSimulation: (versionId: string) => void;
}) {
  const [versionId, setVersionId] = useState(
    initialVersionId ?? versions[0]?.id ?? ""
  );
  const [runs, setRuns] = useState<PredictionRunSummaryDto[]>([]);
  const [activeJobId, setActiveJobId] = useState<string | null>(null);
  const [result, setResult] = useState<PredictionResultDto | null>(null);
  const [running, setRunning] = useState(false);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const trackRef = useRef<JobTrackHandle | null>(null);

  useEffect(() => () => trackRef.current?.cancel(), []);

  // Реагируем на внешнюю передачу версии (кнопки потока на странице формуляции):
  // сброс состояния во время рендера при изменении пропа, без эффекта.
  const [prevInitialVersionId, setPrevInitialVersionId] = useState(initialVersionId);
  if (initialVersionId && initialVersionId !== prevInitialVersionId) {
    setPrevInitialVersionId(initialVersionId);
    setVersionId(initialVersionId);
  }

  // Сброс выбранного результата при смене версии — тоже корректировка состояния
  // во время рендера (React повторно отрендерит до коммита, без каскадного эффекта).
  const [runsVersionId, setRunsVersionId] = useState(versionId);
  if (versionId !== runsVersionId) {
    setRunsVersionId(versionId);
    setResult(null);
    setActiveJobId(null);
    setError(null);
    setRuns([]);
  }

  useEffect(() => {
    if (!versionId) return;
    let cancelled = false;
    api
      .listPredictionRuns(versionId)
      .then((r) => {
        if (!cancelled) setRuns(r);
      })
      .catch(() => {
        if (!cancelled) setRuns([]);
      });
    return () => {
      cancelled = true;
    };
  }, [versionId]);

  async function runPrediction() {
    if (!versionId) return;
    setRunning(true);
    setError(null);
    setResult(null);
    setProgress(0);
    try {
      const job = await api.submitPrediction(versionId);
      setActiveJobId(job.id);
      const handle = trackJob("prediction", job.id, setProgress);
      trackRef.current = handle;
      const trackResult = await handle.done;
      if (trackResult.ok) {
        setResult(await api.getPredictionResult(job.id));
        setRuns(await api.listPredictionRuns(versionId));
      } else {
        setError(trackResult.error ?? "Prediction failed.");
        setRuns(await api.listPredictionRuns(versionId));
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Submission failed");
    } finally {
      setRunning(false);
      setProgress(null);
    }
  }

  async function openRun(run: PredictionRunSummaryDto) {
    if (run.status !== "Completed") return;
    setError(null);
    setActiveJobId(run.jobId);
    setResult(await api.getPredictionResult(run.jobId));
  }

  // После записи review/outcome перезагружаем результат (reviews/outcome) и историю (флаг HasOutcome).
  async function reloadActiveResult() {
    if (!activeJobId) return;
    const [freshResult, freshRuns] = await Promise.all([
      api.getPredictionResult(activeJobId),
      api.listPredictionRuns(versionId),
    ]);
    setResult(freshResult);
    setRuns(freshRuns);
  }

  if (versions.length === 0) {
    return (
      <Card className="border-dashed">
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          Create a version on the Composition tab before running predictions.
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle>Run prediction</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <select
            aria-label="Select version"
            className="h-10 rounded-md border border-input bg-background px-3 text-sm"
            value={versionId}
            onChange={(e) => setVersionId(e.target.value)}
          >
            {versions.map((v) => (
              <option key={v.id} value={v.id}>
                v{v.versionNumber}
              </option>
            ))}
          </select>
          <Button onClick={runPrediction} disabled={running || !versionId}>
            {running ? <Loader2 className="h-4 w-4 animate-spin" /> : <Brain className="h-4 w-4" />}
            Predict
          </Button>
          {progress !== null && running && (
            <p className="text-sm text-muted-foreground">Progress: {progress}%</p>
          )}
          {error && <p className="text-sm text-destructive">{error}</p>}
        </CardContent>
      </Card>

      {result && (
        <Card>
          <CardHeader>
            <CardTitle>Prediction result</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
              <Stat label="Success" value={formatPercent(result.successProbability, 1)} />
              <Stat label="Toxicity" value={formatPercent(result.toxicityScore)} />
              <Stat label="Stability" value={formatPercent(result.stabilityScore)} />
              <div className="rounded-md border p-3">
                <div className="text-xs text-muted-foreground">Side risk</div>
                <div className={`mt-1 text-lg font-bold ${sideRiskColor[result.sideRiskLevel] ?? ""}`}>
                  {result.sideRiskLevel}
                </div>
              </div>
            </div>
            <p className="text-sm">{result.summary}</p>
            {result.rationaleItems?.length > 0 && (
              <div className="space-y-2">
                <h4 className="text-sm font-medium">Rationale</h4>
                {result.rationaleItems.map((r) => (
                  <div key={r.id} className="space-y-1 rounded-md border p-3 text-sm">
                    <div className="flex items-center justify-between">
                      <span className="font-medium">{r.category}</span>
                      <span className="text-xs text-muted-foreground">
                        confidence {formatPercent(r.confidence)}
                      </span>
                    </div>
                    <p className="text-muted-foreground">{r.explanation}</p>
                  </div>
                ))}
              </div>
            )}
            <OutcomePanel
              key={`${result.id}-${result.outcome?.recordedAtUtc ?? "none"}-${result.reviews.length}`}
              result={result}
              onChanged={reloadActiveResult}
            />
            <Button variant="outline" onClick={() => onContinueToSimulation(versionId)}>
              Continue: run simulation <ArrowRight className="h-4 w-4" />
            </Button>
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <History className="h-4 w-4" /> Run history
          </CardTitle>
        </CardHeader>
        <CardContent>
          {runs.length === 0 ? (
            <p className="text-sm text-muted-foreground">No predictions for this version yet.</p>
          ) : (
            <ul className="divide-y" data-testid="prediction-history">
              {runs.map((run) => (
                <li key={run.jobId}>
                  <button
                    type="button"
                    disabled={run.status !== "Completed"}
                    onClick={() => void openRun(run)}
                    className={`flex w-full items-center gap-3 py-2.5 text-left text-sm ${
                      run.status === "Completed" ? "hover:bg-muted/50" : "cursor-default"
                    } ${activeJobId === run.jobId ? "bg-muted/40" : ""}`}
                  >
                    <RunStatusBadge status={run.status} />
                    <span className="flex-1 truncate">
                      {run.status === "Completed"
                        ? `Success ${formatPercent(run.successProbability, 1)} · ${run.modelDisplayName}`
                        : "Prediction run"}
                    </span>
                    {run.hasOutcome && (
                      <CheckCircle2 className="h-4 w-4 text-green-600" aria-label="Lab outcome recorded" />
                    )}
                    <span className="shrink-0 text-xs text-muted-foreground">
                      {formatDateTime(run.createdAtUtc)}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border p-3">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="mt-1 text-lg font-bold">{value}</div>
    </div>
  );
}
