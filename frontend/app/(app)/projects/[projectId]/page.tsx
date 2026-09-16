"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { api, type ProjectDto, type FormulationDto } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Breadcrumbs } from "@/components/layout/breadcrumbs";
import { FormulationDialog } from "@/components/formulations/formulation-dialog";
import { Plus, ChevronRight, FlaskConical } from "lucide-react";

export default function ProjectDetailPage() {
  const params = useParams<{ projectId: string }>();
  const projectId = params.projectId;

  const [project, setProject] = useState<ProjectDto | null>(null);
  const [formulations, setFormulations] = useState<FormulationDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showDialog, setShowDialog] = useState(false);

  // Все setState — только после await; флаг отмены защищает от гонки при смене маршрута.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [p, fs] = await Promise.all([
          api.getProject(projectId),
          api.listFormulations(projectId),
        ]);
        if (cancelled) return;
        setProject(p);
        setFormulations(fs);
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : "Failed to load project");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [projectId]);

  if (loading) return <p className="text-sm text-muted-foreground">Loading…</p>;
  if (error || !project) {
    return (
      <div className="rounded-md border border-destructive bg-destructive/10 px-4 py-3 text-sm text-destructive">
        {error ?? "Project not found."}
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <Breadcrumbs items={[{ label: "Projects", href: "/projects" }, { label: project.name }]} />

      <div className="flex items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold">{project.name}</h1>
          {project.description && (
            <p className="mt-1 text-sm text-muted-foreground">{project.description}</p>
          )}
        </div>
        <Button onClick={() => setShowDialog(true)}>
          <Plus className="h-4 w-4" /> New formulation
        </Button>
      </div>

      {formulations.length === 0 ? (
        <Card className="border-dashed">
          <CardContent className="flex flex-col items-center justify-center py-12 text-center">
            <FlaskConical className="mb-3 h-8 w-8 text-muted-foreground" />
            <p className="text-muted-foreground">
              No formulations yet. Create one and compose its first version.
            </p>
            <Button className="mt-4" onClick={() => setShowDialog(true)}>
              <Plus className="h-4 w-4" /> New formulation
            </Button>
          </CardContent>
        </Card>
      ) : (
        <div className="space-y-3">
          {formulations.map((f) => (
            <Link key={f.id} href={`/projects/${projectId}/formulations/${f.id}`}>
              <Card className="transition-colors hover:border-primary/50 hover:bg-muted/30">
                <CardContent className="flex items-center justify-between gap-4 py-4">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <h2 className="truncate font-semibold">{f.name}</h2>
                      <span className="shrink-0 text-xs text-muted-foreground">
                        v{f.currentVersionNumber}
                      </span>
                    </div>
                    <p className="mt-0.5 truncate text-sm text-muted-foreground">{f.targetPurpose}</p>
                  </div>
                  <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
                </CardContent>
              </Card>
            </Link>
          ))}
        </div>
      )}

      <FormulationDialog
        projectId={projectId}
        open={showDialog}
        onClose={() => setShowDialog(false)}
        onCreated={(f) => setFormulations((prev) => [...prev, f])}
      />
    </div>
  );
}
