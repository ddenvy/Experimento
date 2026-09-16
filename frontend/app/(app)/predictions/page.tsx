"use client";

import { useEffect, useRef, useState } from "react";
import type { HubConnection } from "@microsoft/signalr";
import { api, type PredictionResultDto } from "@/lib/api";
import { subscribeToJob, stopJobConnection } from "@/lib/realtime";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Brain, Loader2 } from "lucide-react";

interface Project { id: string; name: string }
interface Formulation { id: string; name: string }
interface Version { id: string; versionNumber: number }

const sideRiskColor: Record<string, string> = { Low: "text-green-600", Medium: "text-yellow-600", High: "text-red-600" };

export default function PredictionsPage() {
  const [projects, setProjects] = useState<Project[]>([]);
  const [projectId, setProjectId] = useState("");
  const [formulations, setFormulations] = useState<Formulation[]>([]);
  const [formulationId, setFormulationId] = useState("");
  const [versions, setVersions] = useState<Version[]>([]);
  const [versionId, setVersionId] = useState("");
  const [jobId, setJobId] = useState<string | null>(null);
  const [result, setResult] = useState<PredictionResultDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const connectionRef = useRef<HubConnection | null>(null);

  // Закрываем SignalR-соединение при размонтировании.
  useEffect(() => () => {
    void stopJobConnection(connectionRef.current);
  }, []);

  useEffect(() => {
    api.listProjects().then(setProjects).catch(() => {});
  }, []);

  useEffect(() => {
    if (projectId) {
      api.listFormulations(projectId).then(setFormulations).catch(() => setFormulations([]));
    } else {
      setFormulations([]);
    }
    setFormulationId("");
    setVersions([]);
    setVersionId("");
  }, [projectId]);

  useEffect(() => {
    if (formulationId) {
      api.listVersions(formulationId).then(setVersions).catch(() => setVersions([]));
    } else {
      setVersions([]);
    }
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
      const job = await api.submitPrediction(versionId);
      setJobId(job.id);

      const connection = await subscribeToJob("prediction", job.id, {
        onProgress: (p) => setProgress(p),
        onCompleted: async () => {
          setResult(await api.getPredictionResult(job.id));
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
      const current = await api.getPredictionJob(job.id);
      if (current.status === "Completed") {
        setResult(await api.getPredictionResult(job.id));
        setLoading(false);
        setProgress(null);
        await stopJobConnection(connectionRef.current);
      } else if (current.status === "Failed") {
        setError("Prediction failed. Please retry or contact support.");
        setLoading(false);
        setProgress(null);
        await stopJobConnection(connectionRef.current);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Submission failed");
      setLoading(false);
      setProgress(null);
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold">Property Predictions</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Select a formulation version and run an AI property prediction.
        </p>
      </div>

      <Card>
        <CardHeader><CardTitle>Run prediction</CardTitle></CardHeader>
        <CardContent className="space-y-4">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            <select
              className="h-10 rounded-md border border-input bg-background px-3 text-sm"
              value={projectId}
              onChange={(e) => setProjectId(e.target.value)}
            >
              <option value="">Select project…</option>
              {projects.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
            <select
              className="h-10 rounded-md border border-input bg-background px-3 text-sm"
              value={formulationId}
              onChange={(e) => setFormulationId(e.target.value)}
              disabled={!projectId}
            >
              <option value="">Select formulation…</option>
              {formulations.map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}
            </select>
            <select
              className="h-10 rounded-md border border-input bg-background px-3 text-sm"
              value={versionId}
              onChange={(e) => setVersionId(e.target.value)}
              disabled={!formulationId}
            >
              <option value="">Select version…</option>
              {versions.map((v) => <option key={v.id} value={v.id}>v{v.versionNumber}</option>)}
            </select>
          </div>
          <Button onClick={submit} disabled={loading || !versionId}>
            {loading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Brain className="h-4 w-4" />}
            Predict
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
          <CardHeader><CardTitle>Prediction result</CardTitle></CardHeader>
          <CardContent className="space-y-4">
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              <Stat label="Success" value={`${(result.successProbability * 100).toFixed(1)}%`} />
              <Stat label="Toxicity" value={`${(result.toxicityScore * 100).toFixed(0)}%`} />
              <Stat label="Stability" value={`${(result.stabilityScore * 100).toFixed(0)}%`} />
              <div className="rounded-md border p-3">
                <div className="text-xs text-muted-foreground">Side risk</div>
                <div className={`text-lg font-bold mt-1 ${sideRiskColor[result.sideRiskLevel] ?? ""}`}>
                  {result.sideRiskLevel}
                </div>
              </div>
            </div>
            <p className="text-sm">{result.summary}</p>
            {result.rationaleItems?.length > 0 && (
              <div className="space-y-2">
                <h4 className="font-medium text-sm">Rationale</h4>
                {result.rationaleItems.map((r, i) => (
                  <div key={i} className="rounded-md border p-3 text-sm space-y-1">
                    <div className="flex items-center justify-between">
                      <span className="font-medium">{r.category}</span>
                      <span className="text-xs text-muted-foreground">confidence {(r.confidence * 100).toFixed(0)}%</span>
                    </div>
                    <p className="text-muted-foreground">{r.explanation}</p>
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

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border p-3">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="text-lg font-bold mt-1">{value}</div>
    </div>
  );
}
