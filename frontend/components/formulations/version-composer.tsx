"use client";

import { useState } from "react";
import { api, type ChemicalDto, type FormulationVersionDto } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ChemicalPicker } from "@/components/chemical-picker";
import { Plus, Trash2 } from "lucide-react";

// Черновик строки компонента: вещество выбирается строго из каталога PubChem.
interface ComponentDraft {
  chemical: ChemicalDto | null;
  proportion: number;
  role: string;
}

// Форма создания новой версии формуляции (компоненты + условия).
export function VersionComposer({
  formulationId,
  onCreated,
}: {
  formulationId: string;
  onCreated: (version: FormulationVersionDto) => void;
}) {
  const [components, setComponents] = useState<ComponentDraft[]>([
    { chemical: null, proportion: 0, role: "" },
  ]);
  const [temperature, setTemperature] = useState(25);
  const [ph, setPh] = useState(7);
  const [solvent, setSolvent] = useState("water");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    const total = components.reduce((s, c) => s + (Number(c.proportion) || 0), 0);
    if (Math.abs(total - 1) > 0.01) {
      setError(`Sum of proportions must equal 1.0 (got ${total.toFixed(3)})`);
      return;
    }
    if (components.some((c) => c.chemical === null)) {
      setError("Every component must be selected from the chemical catalog (PubChem).");
      return;
    }

    setSaving(true);
    try {
      const v = await api.createVersion(formulationId, {
        components: components.map((c) => ({
          // Сервер берёт эталонные значения из каталога по pubChemCid.
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
        notes: notes || undefined,
      });
      setComponents([{ chemical: null, proportion: 0, role: "" }]);
      setNotes("");
      onCreated(v);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create version");
    } finally {
      setSaving(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3 rounded-md border bg-muted/30 p-4">
      <h4 className="text-sm font-medium">New version</h4>
      <div className="space-y-2">
        <label className="text-sm font-medium">Components (proportions must sum to 1.0)</label>
        {components.map((c, i) => (
          <div key={i} className="flex items-start gap-2 rounded-md border p-2">
            <div className="flex-1">
              <ChemicalPicker
                value={c.chemical}
                onSelect={(chemical) =>
                  setComponents((prev) => prev.map((x, j) => (j === i ? { ...x, chemical } : x)))
                }
                onClear={() =>
                  setComponents((prev) => prev.map((x, j) => (j === i ? { ...x, chemical: null } : x)))
                }
              />
              <div className="mt-2 flex gap-2">
                <Input
                  type="number"
                  step="0.01"
                  placeholder="Proportion"
                  value={c.proportion || ""}
                  onChange={(e) =>
                    setComponents((prev) =>
                      prev.map((x, j) => (j === i ? { ...x, proportion: Number(e.target.value) } : x))
                    )
                  }
                  className="w-32"
                />
                <Input
                  placeholder="Role (optional)"
                  value={c.role}
                  onChange={(e) =>
                    setComponents((prev) =>
                      prev.map((x, j) => (j === i ? { ...x, role: e.target.value } : x))
                    )
                  }
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
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() =>
            setComponents((prev) => [...prev, { chemical: null, proportion: 0, role: "" }])
          }
        >
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
      <Input placeholder="Notes (optional)" value={notes} onChange={(e) => setNotes(e.target.value)} />
      {error && <p className="text-sm text-destructive">{error}</p>}
      <Button type="submit" disabled={saving || components.some((c) => c.chemical === null)}>
        Create version
      </Button>
    </form>
  );
}
