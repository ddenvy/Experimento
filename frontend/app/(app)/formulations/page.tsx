"use client";

import { useEffect, useState } from "react";
import { api, type ChemicalDto, type FormulationVersionDto } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ChemicalPicker } from "@/components/chemical-picker";
import { Plus, Trash2, ChevronDown, ChevronRight, FlaskConical } from "lucide-react";

interface Project {
  id: string;
  name: string;
  description: string | null;
}

interface Formulation {
  id: string;
  name: string;
  targetPurpose: string;
  currentVersionNumber: number;
}

// Черновик строки компонента: вещество выбирается строго из каталога PubChem.
interface ComponentDraft {
  chemical: ChemicalDto | null;
  proportion: number;
  role: string;
}

export default function FormulationsPage() {
  const [projects, setProjects] = useState<Project[]>([]);
  const [selectedProject, setSelectedProject] = useState<string | null>(null);
  const [formulations, setFormulations] = useState<Formulation[]>([]);
  const [expandedFormulation, setExpandedFormulation] = useState<string | null>(null);
  const [versions, setVersions] = useState<Record<string, FormulationVersionDto[]>>({});

  // modals
  const [showNewProject, setShowNewProject] = useState(false);
  const [showNewFormulation, setShowNewFormulation] = useState(false);
  const [showNewVersion, setShowNewVersion] = useState<string | null>(null);

  // form state
  const [projectName, setProjectName] = useState("");
  const [projectDesc, setProjectDesc] = useState("");
  const [formName, setFormName] = useState("");
  const [formPurpose, setFormPurpose] = useState("");
  const [components, setComponents] = useState<ComponentDraft[]>([
    { chemical: null, proportion: 0, role: "" },
  ]);
  const [temperature, setTemperature] = useState(25);
  const [ph, setPh] = useState(7);
  const [solvent, setSolvent] = useState("water");
  const [versionNotes, setVersionNotes] = useState("");

  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.listProjects().then(setProjects).catch(() => {});
  }, []);

  useEffect(() => {
    if (selectedProject) {
      api.listFormulations(selectedProject).then(setFormulations).catch(() => setFormulations([]));
    } else {
      setFormulations([]);
    }
  }, [selectedProject]);

  async function loadVersions(formulationId: string) {
    const v = await api.listVersions(formulationId).catch(() => []);
    setVersions((prev) => ({ ...prev, [formulationId]: v }));
  }

  async function createProject(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      const p = await api.createProject({ name: projectName, description: projectDesc });
      setProjects((prev) => [...prev, p]);
      setSelectedProject(p.id);
      setProjectName("");
      setProjectDesc("");
      setShowNewProject(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create project");
    }
  }

  async function createFormulation(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!selectedProject) return;
    try {
      const f = await api.createFormulation({ projectId: selectedProject, name: formName, targetPurpose: formPurpose });
      setFormulations((prev) => [...prev, f]);
      setFormName("");
      setFormPurpose("");
      setShowNewFormulation(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create formulation");
    }
  }

  async function createVersion(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!showNewVersion) return;
    try {
      const total = components.reduce((s, c) => s + (Number(c.proportion) || 0), 0);
      if (Math.abs(total - 1) > 0.01) {
        setError(`Sum of proportions must equal 1.0 (got ${total.toFixed(3)})`);
        return;
      }
      if (components.some((c) => c.chemical === null)) {
        setError("Every component must be selected from the chemical catalog (PubChem).");
        return;
      }
      const v = await api.createVersion(showNewVersion, {
        components: components.map((c) => ({
          // Имя/CAS/формула/масса дублируются для наглядности, но сервер всё равно
          // берёт эталонные значения из каталога по pubChemCid.
          pubChemCid: c.chemical!.pubChemCid,
          chemicalName: c.chemical!.name,
          casNumber: c.chemical!.casNumber,
          formula: c.chemical!.formula,
          molarMass: c.chemical!.molarMass,
          proportion: Number(c.proportion),
          role: c.role || undefined,
        })),
        conditions: {
          temperatureCelsius: Number(temperature),
          phTarget: ph ? Number(ph) : undefined,
          solvent: solvent || undefined,
        },
        notes: versionNotes || undefined,
      });
      setVersions((prev) => ({ ...prev, [showNewVersion]: [...(prev[showNewVersion] || []), v] }));
      setComponents([{ chemical: null, proportion: 0, role: "" }]);
      setVersionNotes("");
      setShowNewVersion(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create version");
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">Formulations</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Create a project, then formulations, then versions with components.
          </p>
        </div>
        <Button onClick={() => setShowNewProject(true)}>
          <Plus className="h-4 w-4" /> New project
        </Button>
      </div>

      {error && (
        <div className="rounded-md border border-destructive bg-destructive/10 px-4 py-3 text-sm text-destructive">
          {error}
        </div>
      )}

      {/* Project selector */}
      <div className="flex gap-2 flex-wrap items-center">
        <select
          className="h-10 rounded-md border border-input bg-background px-3 text-sm min-w-[240px]"
          value={selectedProject ?? ""}
          onChange={(e) => setSelectedProject(e.target.value || null)}
        >
          <option value="">Select a project…</option>
          {projects.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
        {selectedProject && (
          <Button variant="outline" onClick={() => setShowNewFormulation(true)} disabled={!selectedProject}>
            <Plus className="h-4 w-4" /> New formulation
          </Button>
        )}
      </div>

      {/* New project form */}
      {showNewProject && (
        <Card>
          <CardHeader><CardTitle>New project</CardTitle></CardHeader>
          <CardContent>
            <form onSubmit={createProject} className="space-y-4">
              <Input placeholder="Project name" value={projectName} onChange={(e) => setProjectName(e.target.value)} required />
              <Input placeholder="Description (optional)" value={projectDesc} onChange={(e) => setProjectDesc(e.target.value)} />
              <div className="flex gap-2">
                <Button type="submit" disabled={!projectName.trim()}>Create</Button>
                <Button variant="outline" type="button" onClick={() => setShowNewProject(false)}>Cancel</Button>
              </div>
            </form>
          </CardContent>
        </Card>
      )}

      {/* New formulation form */}
      {showNewFormulation && (
        <Card>
          <CardHeader><CardTitle>New formulation</CardTitle></CardHeader>
          <CardContent>
            <form onSubmit={createFormulation} className="space-y-4">
              <Input placeholder="Name" value={formName} onChange={(e) => setFormName(e.target.value)} required />
              <Input placeholder="Target purpose" value={formPurpose} onChange={(e) => setFormPurpose(e.target.value)} required />
              <div className="flex gap-2">
                <Button type="submit" disabled={!formName.trim()}>Create</Button>
                <Button variant="outline" type="button" onClick={() => setShowNewFormulation(false)}>Cancel</Button>
              </div>
            </form>
          </CardContent>
        </Card>
      )}

      {/* Formulations list */}
      <div className="space-y-3">
        {formulations.map((f) => {
          const isOpen = expandedFormulation === f.id;
          const formVersions = versions[f.id] || [];
          return (
            <Card key={f.id}>
              <CardHeader className="pb-2">
                <div className="flex items-center justify-between">
                  <button
                    className="flex items-center gap-2 text-left"
                    onClick={() => {
                      setExpandedFormulation(isOpen ? null : f.id);
                      if (!isOpen && formVersions.length === 0) loadVersions(f.id);
                    }}
                  >
                    {isOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                    <CardTitle className="text-base">{f.name}</CardTitle>
                    <span className="text-xs text-muted-foreground">· v{f.currentVersionNumber}</span>
                  </button>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => {
                      setExpandedFormulation(f.id);
                      if (formVersions.length === 0) loadVersions(f.id);
                      setShowNewVersion(f.id);
                    }}
                  >
                    <Plus className="h-3 w-3" /> New version
                  </Button>
                </div>
                <p className="text-sm text-muted-foreground">{f.targetPurpose}</p>
              </CardHeader>

              {isOpen && (
                <CardContent className="space-y-3">
                  {showNewVersion === f.id && (
                    <div className="rounded-md border bg-muted/30 p-4 space-y-3">
                      <h4 className="font-medium text-sm">New version</h4>
                      <form onSubmit={createVersion} className="space-y-3">
                        <div className="space-y-2">
                          <label className="text-sm font-medium">Components (proportions must sum to 1.0)</label>
                          {components.map((c, i) => (
                            <div key={i} className="flex gap-2 items-start rounded-md border p-2">
                              <div className="flex-1">
                                <ChemicalPicker
                                  value={c.chemical}
                                  onSelect={(chemical) =>
                                    setComponents((prev) => prev.map((x, j) => j === i ? { ...x, chemical } : x))
                                  }
                                  onClear={() =>
                                    setComponents((prev) => prev.map((x, j) => j === i ? { ...x, chemical: null } : x))
                                  }
                                />
                                <div className="mt-2 flex gap-2">
                                  <Input
                                    type="number"
                                    step="0.01"
                                    placeholder="Proportion"
                                    value={c.proportion || ""}
                                    onChange={(e) => setComponents((prev) => prev.map((x, j) => j === i ? { ...x, proportion: Number(e.target.value) } : x))}
                                    className="w-32"
                                  />
                                  <Input
                                    placeholder="Role (optional)"
                                    value={c.role}
                                    onChange={(e) => setComponents((prev) => prev.map((x, j) => j === i ? { ...x, role: e.target.value } : x))}
                                    className="w-44"
                                  />
                                </div>
                              </div>
                              <Button
                                type="button"
                                variant="ghost"
                                size="sm"
                                onClick={() => setComponents((prev) => prev.filter((_, j) => j !== i))}
                                disabled={components.length === 1}
                              >
                                <Trash2 className="h-4 w-4" />
                              </Button>
                            </div>
                          ))}
                          <Button type="button" variant="outline" size="sm" onClick={() => setComponents((prev) => [...prev, { chemical: null, proportion: 0, role: "" }])}>
                            <Plus className="h-3 w-3" /> Add component
                          </Button>
                        </div>
                        <div className="grid grid-cols-3 gap-2">
                          <div>
                            <label className="text-xs text-muted-foreground">Temperature (°C)</label>
                            <Input type="number" value={temperature} onChange={(e) => setTemperature(Number(e.target.value))} />
                          </div>
                          <div>
                            <label className="text-xs text-muted-foreground">pH target</label>
                            <Input type="number" step="0.1" value={ph} onChange={(e) => setPh(Number(e.target.value))} />
                          </div>
                          <div>
                            <label className="text-xs text-muted-foreground">Solvent</label>
                            <Input value={solvent} onChange={(e) => setSolvent(e.target.value)} />
                          </div>
                        </div>
                        <Input placeholder="Notes (optional)" value={versionNotes} onChange={(e) => setVersionNotes(e.target.value)} />
                        <div className="flex gap-2">
                          <Button type="submit" disabled={components.some((c) => c.chemical === null)}>
                            Create version
                          </Button>
                          <Button type="button" variant="outline" onClick={() => setShowNewVersion(null)}>Cancel</Button>
                        </div>
                      </form>
                    </div>
                  )}

                  {formVersions.length === 0 ? (
                    <p className="text-sm text-muted-foreground">No versions yet. Create the first one.</p>
                  ) : (
                    <div className="space-y-2">
                      {formVersions.map((v) => (
                        <div key={v.id} className="rounded-md border p-3 text-sm space-y-1">
                          <div className="flex items-center justify-between">
                            <span className="font-medium">v{v.versionNumber}</span>
                            <span className="text-xs text-muted-foreground">{v.status}</span>
                          </div>
                          <p className="text-xs text-muted-foreground">
                            {v.components.map((c) =>
                              `${c.chemicalName}${c.formula ? ` ${c.formula}` : ""} (${c.proportion})`
                            ).join(", ")}
                          </p>
                          <p className="text-xs text-muted-foreground">
                            {v.conditions.temperatureCelsius}°C · pH {v.conditions.phTarget ?? "—"} · {v.conditions.solvent ?? "—"}
                          </p>
                          {v.notes && <p className="text-xs italic text-muted-foreground">{v.notes}</p>}
                        </div>
                      ))}
                    </div>
                  )}
                </CardContent>
              )}
            </Card>
          );
        })}
        {selectedProject && formulations.length === 0 && (
          <p className="text-muted-foreground">No formulations yet. Create the first one.</p>
        )}
        {!selectedProject && (
          <Card className="border-dashed">
            <CardContent className="flex flex-col items-center justify-center py-12 text-center">
              <FlaskConical className="h-8 w-8 text-muted-foreground mb-3" />
              <p className="text-muted-foreground">Select a project or create a new one to get started.</p>
            </CardContent>
          </Card>
        )}
      </div>
    </div>
  );
}
