"use client";

import { useState } from "react";
import { api, type FormulationVersionDto, type VersionComparisonDto } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { GitCompare, Loader2 } from "lucide-react";

const changeStyle: Record<string, string> = {
  Added: "bg-green-100 text-green-800",
  Removed: "bg-red-100 text-red-800",
  Modified: "bg-yellow-100 text-yellow-800",
};

function proportionCell(value: number | null): string {
  return value == null ? "—" : `${(value * 100).toFixed(1)}%`;
}

export function VersionCompare({
  formulationId,
  versions,
}: {
  formulationId: string;
  versions: FormulationVersionDto[];
}) {
  const [versionA, setVersionA] = useState(versions.length >= 2 ? versions[versions.length - 2].id : versions[0]?.id ?? "");
  const [versionB, setVersionB] = useState(versions[versions.length - 1]?.id ?? "");
  const [comparison, setComparison] = useState<VersionComparisonDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (versions.length < 2) {
    return (
      <Card className="border-dashed">
        <CardContent className="py-4 text-sm text-muted-foreground">
          Create a second version to compare compositions side by side.
        </CardContent>
      </Card>
    );
  }

  async function compare() {
    if (!versionA || !versionB || versionA === versionB) {
      setError("Select two different versions.");
      return;
    }
    setLoading(true);
    setError(null);
    try {
      setComparison(await api.compareVersions(formulationId, versionA, versionB));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Comparison failed");
    } finally {
      setLoading(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <GitCompare className="h-4 w-4" /> Compare versions
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex flex-wrap items-center gap-2">
          <select
            aria-label="Compare version A"
            className="h-10 rounded-md border border-input bg-background px-3 text-sm"
            value={versionA}
            onChange={(e) => setVersionA(e.target.value)}
          >
            {versions.map((v) => (
              <option key={v.id} value={v.id}>
                v{v.versionNumber}
              </option>
            ))}
          </select>
          <span className="text-sm text-muted-foreground">vs</span>
          <select
            aria-label="Compare version B"
            className="h-10 rounded-md border border-input bg-background px-3 text-sm"
            value={versionB}
            onChange={(e) => setVersionB(e.target.value)}
          >
            {versions.map((v) => (
              <option key={v.id} value={v.id}>
                v{v.versionNumber}
              </option>
            ))}
          </select>
          <Button type="button" variant="outline" size="sm" data-testid="compare-versions" onClick={compare} disabled={loading}>
            {loading && <Loader2 className="h-4 w-4 animate-spin" />}
            Compare
          </Button>
          {error && <span className="text-sm text-destructive">{error}</span>}
        </div>

        {comparison && (
          <div className="space-y-3" data-testid="comparison-result">
            <div className="grid gap-3 sm:grid-cols-2 text-xs text-muted-foreground">
              <ConditionsView title={`v${comparison.versionA.versionNumber}`} v={comparison.versionA} />
              <ConditionsView title={`v${comparison.versionB.versionNumber}`} v={comparison.versionB} />
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b text-left text-xs text-muted-foreground">
                    <th className="py-2 pr-4 font-medium">Component</th>
                    <th className="py-2 pr-4 font-medium">Change</th>
                    <th className="py-2 pr-4 text-right font-medium">
                      v{comparison.versionA.versionNumber}
                    </th>
                    <th className="py-2 pr-4 text-right font-medium">
                      v{comparison.versionB.versionNumber}
                    </th>
                    <th className="py-2 text-right font-medium">Delta</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {comparison.componentDiffs.map((d) => {
                    const delta =
                      d.proportionA != null && d.proportionB != null
                        ? `${((d.proportionB - d.proportionA) * 100).toFixed(1)} pp`
                        : "—";
                    return (
                      <tr key={d.chemicalName} data-change={d.change}>
                        <td className="py-2 pr-4 font-medium">{d.chemicalName}</td>
                        <td className="py-2 pr-4">
                          <span className={`rounded px-1.5 py-0.5 text-xs font-medium ${changeStyle[d.change]}`}>
                            {d.change}
                          </span>
                        </td>
                        <td className="py-2 pr-4 text-right">{proportionCell(d.proportionA)}</td>
                        <td className="py-2 pr-4 text-right">{proportionCell(d.proportionB)}</td>
                        <td className="py-2 text-right text-muted-foreground">{delta}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function ConditionsView({ title, v }: { title: string; v: FormulationVersionDto }) {
  return (
    <div className="rounded-md border p-3">
      <div className="mb-1 font-medium text-foreground">{title}</div>
      <div>
        {v.conditions.temperatureCelsius}°C · pH {v.conditions.phTarget ?? "—"} ·{" "}
        {v.conditions.solvent ?? "—"}
      </div>
    </div>
  );
}
