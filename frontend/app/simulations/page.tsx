"use client";

import { useEffect, useRef, useState } from "react";
import type { HubConnection } from "@microsoft/signalr";
import { api, type SimulationResultDto } from "@/lib/api";
import { subscribeToJob, stopJobConnection } from "@/lib/realtime";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Activity, Loader2 } from "lucide-react";

interface Project { id: string; name: string }
interface Formulation { id: string; name: string }
interface Version { id: string; versionNumber: number }

const TARGET_METRICS = ["success", "stability", "toxicity"] as const;

export default function SimulationsPage() {
  const [projects, setProjects] = useState<Project[]>([]);
  const [projectId, setProjectId] = useState("");
  const [formulations, setFormulations] = useState<Formulation[]>([]);
  const [formulationId, setFormulationId] = useState("");
  const [versions, setVersions] = useState<Version[]>([]);
  const [versionId, setVersionId] = useState("");

  // Параметры симуляции (контракт бэкенда SubmitSimulationRequest).
  const [iterations, setIterations] = useState(100);
  const [varyConcentrations, setVaryConcentrations] = useState(true);
  const [varyTemperature, setVaryTemperature] = useState(true);
  const [varyPh, setVaryPh] = useState(false);
  const [seed, setSeed] = useState(42);
  const [targetMetric, setTargetMetric] = useState<string>("success");

  const [jobId, setJobId] = useState<string | null>(null);
  const [result, setResult] = useState<SimulationResultDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    api.listProjects().then(setProjects).catch(() => {});
  }, []);

  useEffect(() => () => {
    void stopJobConnection(connectionRef.current);
  }, []);

  useEffect(() => {
    if (projectId) {
      api.listFormulations(projectId).then(setFormulations).catch(() => setFormulations([]));
    } else { setFormulations([]); }
    setFormulationId(""); setVersions([]); setVersionId("");
  }, [projectId]);

  useEffect(() => {
    if (formulationId) {
      api.listVersions(formulationId).then(setVersions).catch(() => setVersions([]));
    } else { setVersions([]); }
    setVersionId("");
  }, [formulationId]);

  async function submit() {
    if (!versionId) return;
    setLoading(true);
    setError(null);
    setResult(null);
    setJobId(null);
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
      setJobId(job.id);

      const connection = await subscribeToJob("simulation", job.id, {
        onProgress: (p) => setProgress(p),
        onCompleted: async () => {
          setResult(await api.getSimulationResult(job.id));
          setLoading(false);
          setProgress(null);
          await stopJobConnection(connectionRef.current);
        },
        onFaulted: (message) => {
          setError(message);
          setLoading(false);
          setProgress(null);
          void stopJobConnection(connectionRef.current);
        },
      });
      connectionRef.current = connection;

      // Защита от гонки: задача могла завершиться до установки соединения.
      const current = await api.getSimulationJob(job.id);
      if (current.status === "Completed") {
        setResult(await api.getSimulationResult(job.id));
        setLoading(false);
        setProgress(null);
        await stopJobConnection(connectionRef.current);
      } else if (current.status === "Failed") {
        setError("Simulation failed. Please retry or contact support.");
        setLoading(false);
        setProgress(null);
        await stopJobConnection(connectionRef.current);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Simulation failed");
      setLoading(false);
      setProgress(null);
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold">Simulations</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Run a Monte Carlo simulation on a formulation version to explore parameter space.
        </p>
      </div>

      <Card>
        <CardHeader><CardTitle>Run simulation</CardTitle></CardHeader>
        <CardContent className="space-y-4">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            <select className="h-10 rounded-md border border-input bg-background px-3 text-sm" value={projectId} onChange={(e) => setProjectId(e.target.value)}>
              <option value="">Select project…</option>
              {projects.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
            <select className="h-10 rounded-md border border-input bg-background px-3 text-sm" value={formulationId} onChange={(e) => setFormulationId(e.target.value)} disabled={!projectId}>
              <option value="">Select formulation…</option>
              {formulations.map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}
            </select>
            <select className="h-10 rounded-md border border-input bg-background px-3 text-sm" value={versionId} onChange={(e) => setVersionId(e.target.value)} disabled={!formulationId}>
              <option value="">Select version…</option>
              {versions.map((v) => <option key={v.id} value={v.id}>v{v.versionNumber}</option>)}
            </select>
          </div>

          <div className="flex flex-wrap gap-4 items-end">
            <div className="max-w-[140px]">
              <label className="text-xs text-muted-foreground">Iterations (1–1000)</label>
              <Input type="number" value={iterations} onChange={(e) => setIterations(Number(e.target.value))} min={1} max={1000} />
            </div>
            <div className="max-w-[120px]">
              <label className="text-xs text-muted-foreground">Seed</label>
              <Input type="number" value={seed} onChange={(e) => setSeed(Number(e.target.value))} min={0} />
            </div>
            <div>
              <label className="text-xs text-muted-foreground">Target metric</label>
              <select
                className="h-10 rounded-md border border-input bg-background px-3 text-sm"
                value={targetMetric}
                onChange={(e) => setTargetMetric(e.target.value)}
              >
                {TARGET_METRICS.map((m) => <option key={m} value={m}>{m}</option>)}
              </select>
            </div>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={varyConcentrations} onChange={(e) => setVaryConcentrations(e.target.checked)} />
              Vary concentrations
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={varyTemperature} onChange={(e) => setVaryTemperature(e.target.checked)} />
              Vary temperature
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={varyPh} onChange={(e) => setVaryPh(e.target.checked)} />
              Vary pH
            </label>
          </div>

          <Button onClick={submit} disabled={loading || !versionId}>
            {loading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Activity className="h-4 w-4" />}
            Run simulation
          </Button>
          {jobId && <p className="text-sm text-muted-foreground">Job ID: {jobId}</p>}
          {progress !== null && loading && (
            <p className="text-sm text-muted-foreground">Progress: {progress}%</p>
          )}
          {error && <p className="text-sm text-destructive">{error}</p>}
        </CardContent>
      </Card>

      {result && (
        <Card>
          <CardHeader><CardTitle>Simulation result</CardTitle></CardHeader>
          <CardContent className="space-y-3">
            <div className="grid grid-cols-2 gap-3 text-sm">
              <div><span className="text-muted-foreground">Iterations: </span>{result.iterationsExecuted}</div>
              <div><span className="text-muted-foreground">Summary: </span>{result.summary}</div>
            </div>
            {result.bestCandidate && (
              <div className="rounded-md border p-3 text-sm">
                <div className="font-medium mb-1">Best candidate</div>
                <div className="text-muted-foreground">Success: {(result.bestCandidate.successProbability * 100).toFixed(1)}% · Score: {result.bestCandidate.score}</div>
              </div>
            )}
            {result.topCandidates.length > 0 && (
              <div className="space-y-1">
                <h4 className="font-medium text-sm">Top candidates</h4>
                {result.topCandidates.map((c) => (
                  <div key={c.id} className="text-sm text-muted-foreground">
                    #{c.rank} — success {(c.successProbability * 100).toFixed(1)}% · score {c.score}
                  </div>
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
}
