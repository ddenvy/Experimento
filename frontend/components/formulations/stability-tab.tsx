"use client";

import { useEffect, useState } from "react";
import {
  api,
  type FormulationVersionDto,
  type StabilityAssessmentDto,
  type StabilityConfidence,
  type StabilityStudyDto,
} from "@/lib/api";
import { formatDateTime } from "@/lib/format";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { AlertTriangle, Loader2, Plus, Trash2 } from "lucide-react";

const CONFIDENCE_STYLE: Record<StabilityConfidence, string> = {
  Low: "border-slate-300 text-slate-700 bg-slate-50 dark:bg-slate-900/40",
  Medium: "border-yellow-400 text-yellow-700 bg-yellow-50 dark:bg-yellow-950/30",
  High: "border-green-500 text-green-700 bg-green-50 dark:bg-green-950/30",
};

const DAYS_PER_MONTH = 30.4375;

/** Черновик точки: поля хранятся строками, чтобы пустая форма не превращалась в нули. */
interface PointDraft {
  temperature: string;
  timeDays: string;
  assay: string;
}

const EMPTY_DRAFT: PointDraft = { temperature: "", timeDays: "", assay: "" };

function formatShelfLife(days: number): string {
  return `${(days / DAYS_PER_MONTH).toFixed(1)} months (${days.toFixed(0)} days)`;
}

/**
 * Стабильность и срок годности: ввод экспериментальных точек, расчёт константы скорости
 * по кинетике первого порядка и перенос на 25 °C по уравнению Аррениуса. Данные и результат
 * показываются вместе с предупреждениями и допущениями — расчёт не выдаёт себя за отчёт
 * о стабильности.
 */
export function StabilityTab({
  versions,
  initialVersionId,
}: {
  versions: FormulationVersionDto[];
  initialVersionId?: string;
}) {
  const [versionId, setVersionId] = useState(initialVersionId ?? versions[0]?.id ?? "");
  const [studies, setStudies] = useState<StabilityStudyDto[]>([]);
  const [selectedStudyId, setSelectedStudyId] = useState<string | null>(null);
  const [assessment, setAssessment] = useState<StabilityAssessmentDto | null>(null);

  const [drafts, setDrafts] = useState<PointDraft[]>([{ ...EMPTY_DRAFT }, { ...EMPTY_DRAFT }]);
  const [notes, setNotes] = useState("");
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  // Внешняя передача версии (?version=): корректировка во время рендера, без эффекта.
  const [prevInitialVersionId, setPrevInitialVersionId] = useState(initialVersionId);
  if (initialVersionId && initialVersionId !== prevInitialVersionId) {
    setPrevInitialVersionId(initialVersionId);
    setVersionId(initialVersionId);
  }

  // Смена версии сбрасывает выбранное исследование и его результат — во время рендера.
  const [studiesVersionId, setStudiesVersionId] = useState(versionId);
  if (versionId !== studiesVersionId) {
    setStudiesVersionId(versionId);
    setSelectedStudyId(null);
    setAssessment(null);
    setPendingDeleteId(null);
    setSaveError(null);
  }

  // Подписка на внешнюю систему (API): setState только после await, флаг отмены — от гонки.
  const [reloadToken, setReloadToken] = useState(0);
  useEffect(() => {
    if (!versionId) return;
    let cancelled = false;
    (async () => {
      try {
        const items = await api.listStabilityStudies(versionId);
        if (!cancelled) setStudies(items);
      } catch {
        if (!cancelled) setStudies([]);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [versionId, reloadToken]);

  useEffect(() => {
    if (!selectedStudyId) return;
    let cancelled = false;
    (async () => {
      try {
        const result = await api.getStabilityAssessment(selectedStudyId);
        if (!cancelled) setAssessment(result);
      } catch {
        if (!cancelled) setAssessment(null);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [selectedStudyId]);

  function updateDraft(index: number, field: keyof PointDraft, value: string) {
    setDrafts((prev) => prev.map((draft, i) => (i === index ? { ...draft, [field]: value } : draft)));
  }

  async function saveStudy() {
    setSaving(true);
    setSaveError(null);
    try {
      const points = drafts
        .filter((d) => d.temperature !== "" && d.timeDays !== "" && d.assay !== "")
        .map((d) => ({
          temperatureCelsius: Number(d.temperature),
          timeDays: Number(d.timeDays),
          assayPercent: Number(d.assay),
        }));
      const created = await api.createStabilityStudy(versionId, {
        points,
        notes: notes.trim() || undefined,
      });
      setDrafts([{ ...EMPTY_DRAFT }, { ...EMPTY_DRAFT }]);
      setNotes("");
      setStudies((prev) => [created, ...prev]);
      setSelectedStudyId(created.id);
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : "Failed to save the study");
    } finally {
      setSaving(false);
    }
  }

  async function deleteStudy(studyId: string) {
    setPendingDeleteId(null);
    await api.deleteStabilityStudy(studyId);
    setStudies((prev) => prev.filter((s) => s.id !== studyId));
    if (selectedStudyId === studyId) {
      setSelectedStudyId(null);
      setAssessment(null);
    }
  }

  if (versions.length === 0) {
    return (
      <Card className="border-dashed">
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          Create a version on the Composition tab before adding stability data.
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Stability data</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-wrap items-end gap-4">
            <div>
              <label className="text-xs text-muted-foreground">Version</label>
              <select
                aria-label="Select version"
                className="block h-10 rounded-md border border-input bg-background px-3 text-sm"
                value={versionId}
                onChange={(e) => setVersionId(e.target.value)}
              >
                {versions.map((v) => (
                  <option key={v.id} value={v.id}>
                    v{v.versionNumber}
                  </option>
                ))}
              </select>
            </div>
            <div className="min-w-[220px] flex-1">
              <label className="text-xs text-muted-foreground">Notes (optional)</label>
              <Input
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                placeholder="Long-term 25 °C, accelerated 40 °C"
              />
            </div>
          </div>

          <div className="space-y-2" data-testid="stability-points">
            <div className="grid grid-cols-[1fr_1fr_1fr_auto] gap-2 text-xs text-muted-foreground">
              <span>Temperature (°C)</span>
              <span>Time (days)</span>
              <span>Assay (%)</span>
              <span />
            </div>
            {drafts.map((draft, index) => (
              <div key={index} className="grid grid-cols-[1fr_1fr_1fr_auto] items-center gap-2">
                <Input
                  aria-label={`Temperature ${index + 1}`}
                  value={draft.temperature}
                  onChange={(e) => updateDraft(index, "temperature", e.target.value)}
                  inputMode="decimal"
                />
                <Input
                  aria-label={`Time ${index + 1}`}
                  value={draft.timeDays}
                  onChange={(e) => updateDraft(index, "timeDays", e.target.value)}
                  inputMode="decimal"
                />
                <Input
                  aria-label={`Assay ${index + 1}`}
                  value={draft.assay}
                  onChange={(e) => updateDraft(index, "assay", e.target.value)}
                  inputMode="decimal"
                />
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={`Remove point ${index + 1}`}
                  disabled={drafts.length <= 1}
                  onClick={() => setDrafts((prev) => prev.filter((_, i) => i !== index))}
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            ))}
            <p className="text-[11px] text-muted-foreground">
              Assay is the content of the active substance as a percentage of the initial value, so a point
              at time zero is 100%.
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Button variant="outline" size="sm" onClick={() => setDrafts((prev) => [...prev, { ...EMPTY_DRAFT }])}>
              <Plus className="h-4 w-4" /> Add point
            </Button>
            <Button size="sm" onClick={saveStudy} disabled={saving} data-testid="save-stability-study">
              {saving && <Loader2 className="h-4 w-4 animate-spin" />}
              Save study
            </Button>
          </div>
          {saveError && <p className="text-sm text-destructive">{saveError}</p>}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Studies for this version</CardTitle>
        </CardHeader>
        <CardContent>
          {loading ? (
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" /> Loading…
            </div>
          ) : studies.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              No stability data yet. Add measurement points above to project a shelf life.
            </p>
          ) : (
            <ul className="divide-y" data-testid="stability-study-list">
              {studies.map((study) => (
                <li key={study.id} className="flex items-center gap-3 py-2">
                  <button
                    type="button"
                    onClick={() => setSelectedStudyId(study.id)}
                    className={`flex-1 text-left text-sm ${selectedStudyId === study.id ? "font-medium" : ""}`}
                  >
                    v{study.versionNumber} · {study.points.length} points
                    {study.notes ? ` · ${study.notes}` : ""}
                    <span className="ml-2 text-xs text-muted-foreground">
                      {formatDateTime(study.createdAtUtc)}
                    </span>
                  </button>
                  {pendingDeleteId === study.id ? (
                    <Button variant="destructive" size="sm" onClick={() => void deleteStudy(study.id)}>
                      Confirm delete
                    </Button>
                  ) : (
                    <Button
                      variant="ghost"
                      size="sm"
                      aria-label="Delete study"
                      onClick={() => setPendingDeleteId(study.id)}
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

      {assessment && (
        <Card data-testid="stability-assessment">
          <CardHeader>
            <CardTitle className="flex flex-wrap items-center gap-2 text-base">
              Shelf life projection
              <span
                className={`rounded border px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide ${CONFIDENCE_STYLE[assessment.confidence]}`}
              >
                {assessment.confidence} confidence
              </span>
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <p className="text-sm" data-testid="stability-summary">
              {assessment.summary}
            </p>

            <div className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-3">
              <div>
                <span className="text-muted-foreground">Shelf life (t90, 25 °C): </span>
                <span data-testid="stability-shelf-life">
                  {assessment.shelfLifeDaysAt25C === null
                    ? "—"
                    : formatShelfLife(assessment.shelfLifeDaysAt25C)}
                </span>
              </div>
              <div>
                <span className="text-muted-foreground">Activation energy: </span>
                {assessment.activationEnergyKjPerMol === null
                  ? "—"
                  : `${assessment.activationEnergyKjPerMol} kJ/mol`}
              </div>
            </div>

            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-xs text-muted-foreground">
                    <th className="py-1 font-normal">Temperature</th>
                    <th className="py-1 font-normal">Measurements</th>
                    <th className="py-1 font-normal">k (1/day)</th>
                    <th className="py-1 font-normal">R²</th>
                    <th className="py-1 font-normal">t90 here</th>
                  </tr>
                </thead>
                <tbody className="tabular-nums">
                  {assessment.rates.map((rate) => (
                    <tr key={rate.temperatureCelsius} className="border-t">
                      <td className="py-1">{rate.temperatureCelsius} °C</td>
                      <td className="py-1">{rate.measurements}</td>
                      <td className="py-1">{rate.rateConstantPerDay}</td>
                      <td className="py-1">{rate.rSquared ?? "—"}</td>
                      <td className="py-1">
                        {rate.shelfLifeDays === null ? "—" : formatShelfLife(rate.shelfLifeDays)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {assessment.warnings.length > 0 && (
              <div className="rounded border border-yellow-400 bg-yellow-50 p-3 dark:bg-yellow-950/30">
                <div className="flex items-center gap-1.5 text-xs font-medium text-yellow-800 dark:text-yellow-200">
                  <AlertTriangle className="h-3.5 w-3.5" /> Data quality
                </div>
                <ul className="mt-1 space-y-0.5 text-xs text-yellow-800 dark:text-yellow-200">
                  {assessment.warnings.map((warning) => (
                    <li key={warning}>· {warning}</li>
                  ))}
                </ul>
              </div>
            )}

            <details className="text-xs text-muted-foreground">
              <summary className="cursor-pointer">Assumptions behind this projection</summary>
              <ul className="mt-1 space-y-0.5">
                {assessment.assumptions.map((assumption) => (
                  <li key={assumption}>· {assumption}</li>
                ))}
              </ul>
            </details>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
