"use client";

import { Suspense, useCallback, useEffect, useState } from "react";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { api, type ProjectDto, type FormulationDto, type FormulationVersionDto } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Breadcrumbs, type Crumb } from "@/components/layout/breadcrumbs";
import { VersionComposer } from "@/components/formulations/version-composer";
import { VersionCompare } from "@/components/formulations/version-compare";
import { PredictionsTab } from "@/components/predictions/predictions-tab";
import { SimulationsTab } from "@/components/simulations/simulations-tab";
import { Plus, FlaskConical } from "lucide-react";
import { cn } from "@/lib/utils";

const TABS = [
  { id: "composition", label: "Composition" },
  { id: "predictions", label: "Predictions" },
  { id: "simulations", label: "Simulations" },
] as const;

type TabId = (typeof TABS)[number]["id"];

function FormulationDetail() {
  const params = useParams<{ projectId: string; formulationId: string }>();
  const { projectId, formulationId } = params;
  const router = useRouter();
  const searchParams = useSearchParams();

  const tab = (searchParams.get("tab") as TabId) || "composition";
  const selectedVersionParam = searchParams.get("version") ?? undefined;

  const [project, setProject] = useState<ProjectDto | null>(null);
  const [formulation, setFormulation] = useState<FormulationDto | null>(null);
  const [versions, setVersions] = useState<FormulationVersionDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showComposer, setShowComposer] = useState(false);

  const load = useCallback(async () => {
    try {
      const [p, f, vs] = await Promise.all([
        api.getProject(projectId),
        api
          .listFormulations(projectId)
          .then((fs) => fs.find((x) => x.id === formulationId) ?? null),
        api.listVersions(formulationId),
      ]);
      if (!f) throw new Error("Formulation not found.");
      setProject(p);
      setFormulation(f);
      setVersions(vs);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load formulation");
    } finally {
      setLoading(false);
    }
  }, [projectId, formulationId]);

  useEffect(() => {
    void load();
  }, [load]);

  function setTab(next: TabId) {
    const qs = new URLSearchParams(searchParams.toString());
    qs.set("tab", next);
    router.replace(`?${qs.toString()}`, { scroll: false });
  }

  function continueToSimulation(versionId: string) {
    const qs = new URLSearchParams(searchParams.toString());
    qs.set("tab", "simulations");
    qs.set("version", versionId);
    router.replace(`?${qs.toString()}`, { scroll: false });
  }

  if (loading) return <p className="text-sm text-muted-foreground">Loading…</p>;
  if (error || !project || !formulation) {
    return (
      <div className="rounded-md border border-destructive bg-destructive/10 px-4 py-3 text-sm text-destructive">
        {error ?? "Formulation not found."}
      </div>
    );
  }

  const crumbs: Crumb[] = [
    { label: "Projects", href: "/projects" },
    { label: project.name, href: `/projects/${projectId}` },
    { label: formulation.name },
  ];

  return (
    <div className="space-y-6">
      <Breadcrumbs items={crumbs} />

      <div>
        <h1 className="text-2xl font-bold">{formulation.name}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{formulation.targetPurpose}</p>
      </div>

      <div className="flex gap-1 border-b" role="tablist" aria-label="Formulation sections">
        {TABS.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={tab === t.id}
            onClick={() => setTab(t.id)}
            className={cn(
              "-mb-px border-b-2 px-4 py-2.5 text-sm font-medium transition-colors",
              tab === t.id
                ? "border-primary text-foreground"
                : "border-transparent text-muted-foreground hover:text-foreground"
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === "composition" && (
        <div className="space-y-4">
          <VersionCompare formulationId={formulationId} versions={versions} />

          <div className="flex justify-end">
            <Button onClick={() => setShowComposer((v) => !v)} variant={showComposer ? "outline" : "default"}>
              <Plus className="h-4 w-4" /> New version
            </Button>
          </div>

          {showComposer && (
            <VersionComposer
              formulationId={formulationId}
              onCreated={(v) => {
                setVersions((prev) => [...prev, v]);
                setShowComposer(false);
              }}
            />
          )}

          {versions.length === 0 ? (
            <Card className="border-dashed">
              <CardContent className="flex flex-col items-center justify-center py-12 text-center">
                <FlaskConical className="mb-3 h-8 w-8 text-muted-foreground" />
                <p className="text-muted-foreground">
                  No versions yet. Compose the first version with catalog-verified components.
                </p>
              </CardContent>
            </Card>
          ) : (
            <div className="space-y-3">
              {versions.map((v) => (
                <Card key={v.id}>
                  <CardContent className="space-y-2 py-4 text-sm">
                    <div className="flex items-center justify-between">
                      <span className="font-medium">v{v.versionNumber}</span>
                      <span className="text-xs text-muted-foreground">{v.status}</span>
                    </div>
                    <p className="text-xs text-muted-foreground">
                      {v.components
                        .map((c) => `${c.chemicalName}${c.formula ? ` ${c.formula}` : ""} (${c.proportion})`)
                        .join(", ")}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {v.conditions.temperatureCelsius}°C · pH {v.conditions.phTarget ?? "—"} ·{" "}
                      {v.conditions.solvent ?? "—"}
                    </p>
                    {v.notes && <p className="text-xs italic text-muted-foreground">{v.notes}</p>}
                  </CardContent>
                </Card>
              ))}
            </div>
          )}
        </div>
      )}

      {tab === "predictions" && (
        <PredictionsTab
          key="predictions"
          versions={versions}
          initialVersionId={selectedVersionParam}
          onContinueToSimulation={continueToSimulation}
        />
      )}

      {tab === "simulations" && (
        <SimulationsTab key="simulations" versions={versions} initialVersionId={selectedVersionParam} />
      )}
    </div>
  );
}

export default function FormulationDetailPage() {
  // Граница Suspense нужна для useSearchParams при клиентской навигации.
  return (
    <Suspense fallback={<p className="text-sm text-muted-foreground">Loading…</p>}>
      <FormulationDetail />
    </Suspense>
  );
}
