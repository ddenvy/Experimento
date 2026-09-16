"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { api, type ProjectDto, type FormulationDto } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { ProjectDialog } from "@/components/projects/project-dialog";
import { Plus, FolderKanban, ChevronRight, FlaskConical } from "lucide-react";

export default function ProjectsPage() {
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [counts, setCounts] = useState<Record<string, number>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showDialog, setShowDialog] = useState(false);

  useEffect(() => {
    void (async () => {
      try {
        const ps = await api.listProjects();
        setProjects(ps);
        const entries = await Promise.all(
          ps.map(async (p): Promise<[string, number]> => {
            const fs = await api.listFormulations(p.id).catch(() => [] as FormulationDto[]);
            return [p.id, fs.length];
          })
        );
        setCounts(Object.fromEntries(entries));
      } catch (e) {
        setError(e instanceof Error ? e.message : "Failed to load projects");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold">Projects</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Each project groups formulations, predictions, simulations and knowledge.
          </p>
        </div>
        <Button onClick={() => setShowDialog(true)}>
          <Plus className="h-4 w-4" /> New project
        </Button>
      </div>

      {error && (
        <div className="rounded-md border border-destructive bg-destructive/10 px-4 py-3 text-sm text-destructive">
          {error}
        </div>
      )}

      {loading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : projects.length === 0 ? (
        <Card className="border-dashed">
          <CardContent className="flex flex-col items-center justify-center py-12 text-center">
            <FolderKanban className="mb-3 h-8 w-8 text-muted-foreground" />
            <p className="text-muted-foreground">No projects yet. Create your first one to get started.</p>
            <Button className="mt-4" onClick={() => setShowDialog(true)}>
              <Plus className="h-4 w-4" /> New project
            </Button>
          </CardContent>
        </Card>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {projects.map((p) => (
            <Link key={p.id} href={`/projects/${p.id}`} className="block">
              <Card className="h-full transition-colors hover:border-primary/50 hover:bg-muted/30">
                <CardContent className="pt-6">
                  <div className="flex items-start justify-between gap-2">
                    <h2 className="font-semibold">{p.name}</h2>
                    <ChevronRight className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" />
                  </div>
                  {p.description && (
                    <p className="mt-1 line-clamp-2 text-sm text-muted-foreground">{p.description}</p>
                  )}
                  <p className="mt-4 flex items-center gap-1.5 text-xs text-muted-foreground">
                    <FlaskConical className="h-3.5 w-3.5" />
                    {counts[p.id] ?? 0} formulation{counts[p.id] === 1 ? "" : "s"}
                  </p>
                </CardContent>
              </Card>
            </Link>
          ))}
        </div>
      )}

      <ProjectDialog
        open={showDialog}
        onClose={() => setShowDialog(false)}
        onCreated={(p) => setProjects((prev) => [...prev, p])}
      />
    </div>
  );
}
