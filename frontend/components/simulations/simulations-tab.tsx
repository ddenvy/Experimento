"use client";

import { useEffect, useRef, useState } from "react";
import {
  api,
  type FormulationVersionDto,
  type SimulationResultDto,
  type SimulationRunSummaryDto,
} from "@/lib/api";
import { trackJob, type JobTrackHandle } from "@/lib/track-job";
import { formatDateTime, formatPercent } from "@/lib/format";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Activity, Loader2, History } from "lucide-react";
import { RunStatusBadge } from "@/components/runs/run-status-badge";

const TARGET_METRICS = ["success", "stability", "toxicity"] as const;

export function SimulationsTab({
  versions,
  initialVersionId,
}: {
  versions: FormulationVersionDto[];
  initialVersionId?: string;
}) {
  const [versionId, setVersionId] = useState(initialVersionId ?? versions[0]?.id ?? "");
  const [iterations, setIterations] = useState(100);
  const [varyConcentrations, setVaryConcentrations] = useState(true);
  const [varyTemperature, setVaryTemperature] = useState(true);
  const [varyPh, setVaryPh] = useState(false);
  const [seed, setSeed] = useState(42);
  const [targetMetric, setTargetMetric] = useState<string>("success");

  const [runs, setRuns] = useState<SimulationRunSummaryDto[]>([]);
  const [activeJobId, setActiveJobId] = useState<string | null>(null);
  const [result, setResult] = useState<SimulationResultDto | null>(null);
  const [running, setRunning] = useState(false);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const trackRef = useRef<JobTrackHandle | null>(null);

  useEffect(() => () => trackRef.current?.cancel(), []);

  useEffect(() => {
    if (initialVersionId) setVersionId(initialVersionId);
  }, [initialVersionId]);

  useEffect(() => {
    setResult(null);
    setActiveJobId(null);
    setError(null);
    if (!versionId) {
      setRuns([]);
      return;
    }
    api.listSimulationRuns(versionId).then(setRuns).catch(() => setRuns([]));
  }, [versionId]);

  async function runSimulation() {
    if (!versionId) return;
    setRunning(true);
    setError(null);
    setResult(null);
    setProgress(0);
    try {
      const job = await api.submitSimulation(versionId, {
        iterations: Number(iterations),
        varyConcentrations,
        varyTemperature,
        varyPh,
        seed: Number(seed),
        targetMetric,
      });
      setActiveJobId(job.id);
      const handle = trackJob("simulation", job.id, setProgress);
      trackRef.current = handle;
      const trackResult = await handle.done;
      if (trackResult.ok) {
        setResult(await api.getSimulationResult(job.id));
        setRuns(await api.listSimulationRuns(versionId));
      } else {
        setError(trackResult.error ?? "Simulation failed.");
        setRuns(await api.listSimulationRuns(versionId));
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Simulation failed");
    } finally {
      setRunning(false);
      setProgress(null);
    }
  }

  async function openRun(run: SimulationRunSummaryDto) {
    if (run.status !== "Completed") return;
    setError(null);
    setActiveJobId(run.jobId);
    setResult(await api.getSimulationResult(run.jobId));
  }

  if (versions.length === 0) {
    return (
      <Card className="border-dashed">
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          Create a version on the Composition tab before running simulations.
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle>Run simulation</CardTitle>
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

          <div className="flex flex-wrap items-end gap-4">
            <div className="max-w-[140px]">
              <label className="text-xs text-muted-foreground">Iterations (1–1000)</label>
              <Input
                type="number"
                value={iterations}
                onChange={(e) => setIterations(Number(e.target.value))}
                min={1}
                max={1000}
              />
            </div>
            <div className="max-w-[120px]">
              <label className="text-xs text-muted-foreground">Seed</label>
              <Input
                type="number"
                value={seed}
                onChange={(e) => setSeed(Number(e.target.value))}
                min={0}
              />
            </div>
            <div>
              <label className="text-xs text-muted-foreground">Target metric</label>
              <select
                className="h-10 rounded-md border border-input bg-background px-3 text-sm"
                value={targetMetric}
                onChange={(e) => setTargetMetric(e.target.value)}
              >
                {TARGET_METRICS.map((m) => (
                  <option key={m} value={m}>
                    {m}
                  </option>
                ))}
              </select>
            </div>
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={varyConcentrations}
                onChange={(e) => setVaryConcentrations(e.target.checked)}
              />
              Vary concentrations
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={varyTemperature}
                onChange={(e) => setVaryTemperature(e.target.checked)}
              />
              Vary temperature
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={varyPh} onChange={(e) => setVaryPh(e.target.checked)} />
              Vary pH
            </label>
          </div>

          <Button onClick={runSimulation} disabled={running || !versionId}>
            {running ? <Loader2 className="h-4 w-4 animate-spin" /> : <Activity className="h-4 w-4" />}
            Run simulation
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
            <CardTitle>Simulation result</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="grid grid-cols-2 gap-3 text-sm">
              <div>
                <span className="text-muted-foreground">Iterations: </span>
                {result.iterationsExecuted}
              </div>
              <div>
                <span className="text-muted-foreground">Summary: </span>
                {result.summary}
              </div>
            </div>
            {result.bestCandidate && (
              <div className="rounded-md border p-3 text-sm">
                <div className="mb-1 font-medium">Best candidate</div>
                <div className="text-muted-foreground">
                  Success: {formatPercent(result.bestCandidate.successProbability, 1)} · Score:{" "}
                  {result.bestCandidate.score.toFixed(3)}
                </div>
              </div>
            )}
            {result.topCandidates.length > 0 && (
              <div className="space-y-1">
                <h4 className="text-sm font-medium">Top candidates</h4>
                {result.topCandidates.map((c) => (
                  <div key={c.id} className="text-sm text-muted-foreground">
                    #{c.rank} — success {formatPercent(c.successProbability, 1)} · score{" "}
                    {c.score.toFixed(3)}
                  </div>
                ))}
              </div>
            )}
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
            <p className="text-sm text-muted-foreground">No simulations for this version yet.</p>
          ) : (
            <ul className="divide-y">
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
                        ? `${run.iterationsExecuted} iterations${
                            run.bestSuccessProbability != null
                              ? ` · best success ${formatPercent(run.bestSuccessProbability, 1)}`
                              : ""
                          }`
                        : "Simulation run"}
                    </span>
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
