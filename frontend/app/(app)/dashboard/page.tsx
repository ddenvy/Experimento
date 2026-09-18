"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import {
  api,
  type ProjectDto,
  type CalibrationStatsDto,
  type RecentRunDto,
  type KnowledgeDocumentDto,
} from "@/lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { buttonVariants } from "@/components/ui/button";
import { RunStatusBadge } from "@/components/runs/run-status-badge";
import { formatDateTime, formatPercent } from "@/lib/format";
import {
  FolderKanban,
  Brain,
  FlaskConical,
  BookOpen,
  Gauge,
  ArrowRight,
  Plus,
  Sparkles,
  Loader2,
} from "lucide-react";

export default function DashboardPage() {
  const router = useRouter();
  const [projects, setProjects] = useState<ProjectDto[] | null>(null);
  const [calibration, setCalibration] = useState<CalibrationStatsDto | null>(null);
  const [recent, setRecent] = useState<RecentRunDto[] | null>(null);
  const [documents, setDocuments] = useState<KnowledgeDocumentDto[] | null>(null);
  const [provisioning, setProvisioning] = useState(false);
  const [provisionError, setProvisionError] = useState<string | null>(null);

  useEffect(() => {
    api.listProjects().then(setProjects).catch(() => setProjects([]));
    api.getCalibration().then(setCalibration).catch(() => null);
    api.getRecentRuns().then(setRecent).catch(() => setRecent([]));
    api.listDocuments().then(setDocuments).catch(() => setDocuments([]));
  }, []);

  // Онбординг: одной кнопкой разворачиваем готовый demo-проект с полным циклом R&D.
  async function loadDemo() {
    setProvisioning(true);
    setProvisionError(null);
    try {
      const demo = await api.provisionDemoData();
      router.push(`/projects/${demo.projectId}/formulations/${demo.formulationId}`);
    } catch (e) {
      setProvisionError(e instanceof Error ? e.message : "Failed to create the demo project");
      setProvisioning(false);
    }
  }

  const stats = [
    { label: "Projects", value: projects?.length ?? "—", icon: FolderKanban },
    { label: "Predictions run", value: calibration?.total ?? "—", icon: Brain },
    { label: "Lab outcomes", value: calibration?.withOutcome ?? "—", icon: FlaskConical },
    { label: "Documents indexed", value: documents?.length ?? "—", icon: BookOpen },
  ];

  return (
    <div className="space-y-8">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Dashboard</h1>
          <p className="mt-1 text-muted-foreground">
            Pick up where you left off — your latest formulation runs at a glance.
          </p>
        </div>
        <Link href="/projects" className={buttonVariants()}>
          <Plus className="h-4 w-4" /> New project
        </Link>
      </div>

      {projects !== null && projects.length === 0 && (
        <Card
          data-testid="onboarding-demo"
          className="border-cyan-400/30 bg-gradient-to-br from-cyan-400/[0.07] to-transparent"
        >
          <CardContent className="flex flex-col gap-4 py-6 sm:flex-row sm:items-center sm:justify-between">
            <div className="flex items-start gap-3">
              <span className="mt-0.5 inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-cyan-400/30 bg-cyan-400/10">
                <Sparkles className="h-4 w-4 text-cyan-300" />
              </span>
              <div>
                <h2 className="text-base font-semibold">New here? Explore a ready-made R&amp;D project</h2>
                <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
                  Load a demo aspirin tablet formulation with a prediction, expert review, recorded lab
                  outcome, stress-test simulation, Arrhenius shelf-life study and indexed documents —
                  the full scientific cycle in one click.
                </p>
                {provisionError && (
                  <p className="mt-2 text-sm text-destructive">{provisionError}</p>
                )}
              </div>
            </div>
            <button
              type="button"
              data-testid="load-demo"
              onClick={loadDemo}
              disabled={provisioning}
              className="inline-flex shrink-0 items-center justify-center gap-2 rounded-lg bg-cyan-500 px-5 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-cyan-400 disabled:opacity-60"
            >
              {provisioning && <Loader2 className="h-4 w-4 animate-spin" />}
              Load demo project
            </button>
          </CardContent>
        </Card>
      )}

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {stats.map((s) => {
          const Icon = s.icon;
          return (
            <Card key={s.label}>
              <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">{s.label}</CardTitle>
                <Icon className="h-4 w-4 text-muted-foreground" />
              </CardHeader>
              <CardContent>
                <div className="text-2xl font-bold">{s.value}</div>
              </CardContent>
            </Card>
          );
        })}
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>Recent runs</CardTitle>
          </CardHeader>
          <CardContent>
            {recent === null ? (
              <p className="text-sm text-muted-foreground">Loading…</p>
            ) : recent.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                No runs yet. Open a formulation and run your first prediction.
              </p>
            ) : (
              <ul className="divide-y">
                {recent.map((run) => (
                  <li key={`${run.kind}-${run.jobId}`}>
                    <Link
                      href={`/projects/${run.projectId}/formulations/${run.formulationId}?tab=${
                        run.kind === "prediction" ? "predictions" : "simulations"
                      }`}
                      className="flex items-center gap-3 py-2.5 text-sm hover:bg-muted/40"
                    >
                      <RunStatusBadge status={run.status} />
                      <span className="min-w-0 flex-1">
                        <span className="block truncate font-medium">
                          {run.formulationName} · v{run.versionNumber}
                        </span>
                        <span className="block truncate text-xs text-muted-foreground">
                          {run.kind === "prediction" ? "Prediction" : "Simulation"} · {run.projectName}
                          {run.metric != null && ` · ${formatPercent(run.metric, 1)} success`}
                        </span>
                      </span>
                      <span className="shrink-0 text-xs text-muted-foreground">
                        {formatDateTime(run.createdAtUtc)}
                      </span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Quick access</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <Link
              href="/projects"
              className="flex items-center justify-between rounded-md border p-3 text-sm hover:bg-muted/40"
            >
              <span className="flex items-center gap-2">
                <FolderKanban className="h-4 w-4 text-muted-foreground" /> Projects &amp; formulations
              </span>
              <ArrowRight className="h-4 w-4 text-muted-foreground" />
            </Link>
            <Link
              href="/knowledge"
              className="flex items-center justify-between rounded-md border p-3 text-sm hover:bg-muted/40"
            >
              <span className="flex items-center gap-2">
                <BookOpen className="h-4 w-4 text-muted-foreground" /> Knowledge Base
              </span>
              <ArrowRight className="h-4 w-4 text-muted-foreground" />
            </Link>
            <Link
              href="/models"
              className="flex items-center justify-between rounded-md border p-3 text-sm hover:bg-muted/40"
            >
              <span className="flex items-center gap-2">
                <Gauge className="h-4 w-4 text-muted-foreground" /> Model Scorecard
              </span>
              <ArrowRight className="h-4 w-4 text-muted-foreground" />
            </Link>
            {calibration && calibration.withOutcome > 0 && (
              <div className="rounded-md border p-3 text-sm text-muted-foreground">
                Prediction calibration: mean absolute error{" "}
                <span className="font-medium text-foreground">
                  {formatPercent(calibration.meanError, 1)}
                </span>{" "}
                across {calibration.withOutcome} lab outcome{calibration.withOutcome === 1 ? "" : "s"}.
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
