"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { api, type CalibrationStatsDto, type ModelScorecardDto } from "@/lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { buttonVariants } from "@/components/ui/button";
import { formatPercent } from "@/lib/format";
import { Brain, FlaskConical, Gauge, Scale, ArrowRight } from "lucide-react";

// Минимум исходов для доверительной интерпретации ошибки калибровки.
const MIN_OUTCOMES = 5;

export default function ModelScorecardPage() {
  const [calibration, setCalibration] = useState<CalibrationStatsDto | null>(null);
  const [rows, setRows] = useState<ModelScorecardDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([api.getCalibration(), api.getModelScorecard()])
      .then(([cal, scorecard]) => {
        setCalibration(cal);
        setRows(scorecard);
      })
      .catch((e) => setError(e instanceof Error ? e.message : "Failed to load scorecard"));
  }, []);

  return (
    <div className="space-y-8">
      <div>
        <h1 className="text-3xl font-bold tracking-tight">Model Scorecard</h1>
        <p className="mt-1 text-muted-foreground">
          Prediction quality measured against real lab outcomes you recorded.
        </p>
      </div>

      {error && (
        <div className="rounded-md border border-destructive bg-destructive/10 px-4 py-3 text-sm text-destructive">
          {error}
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard label="Predictions run" value={calibration?.total ?? "—"} icon={Brain} />
        <StatCard label="Lab outcomes" value={calibration?.withOutcome ?? "—"} icon={FlaskConical} />
        <StatCard
          label="Mean error"
          value={calibration ? formatPercent(calibration.meanError, 1) : "—"}
          icon={Gauge}
        />
        <StatCard
          label="Mean bias"
          value={calibration && calibration.withOutcome > 0 ? formatPercent(calibration.meanBias, 1) : "—"}
          icon={Scale}
        />
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Models</CardTitle>
        </CardHeader>
        <CardContent>
          {rows === null ? (
            <p className="text-sm text-muted-foreground">Loading…</p>
          ) : rows.length === 0 ? (
            <div className="space-y-3 py-6 text-center">
              <p className="text-sm text-muted-foreground">
                No models registered yet. Run a prediction to register the active model.
              </p>
              <Link href="/projects" className={buttonVariants({ variant: "outline", size: "sm" })}>
                Go to projects <ArrowRight className="h-4 w-4" />
              </Link>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm" data-testid="model-scorecard-table">
                <thead>
                  <tr className="border-b text-left text-xs text-muted-foreground">
                    <th className="py-2 pr-4 font-medium">Model</th>
                    <th className="py-2 pr-4 font-medium">Context of use</th>
                    <th className="py-2 pr-4 text-right font-medium">Predictions</th>
                    <th className="py-2 pr-4 text-right font-medium">Outcomes</th>
                    <th className="py-2 pr-4 text-right font-medium">Mean error</th>
                    <th className="py-2 text-right font-medium">Bias</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {rows.map((m) => (
                    <tr key={m.modelId} data-model-id={m.modelId}>
                      <td className="py-2.5 pr-4">
                        <div className="font-medium">{m.displayName}</div>
                      </td>
                      <td className="max-w-[16rem] truncate py-2.5 pr-4 text-muted-foreground" title={m.contextOfUse}>
                        {m.contextOfUse || "—"}
                      </td>
                      <td className="py-2.5 pr-4 text-right">{m.total}</td>
                      <td className="py-2.5 pr-4 text-right">
                        <span className="inline-flex items-center gap-2">
                          {m.withOutcome}
                          {m.withOutcome > 0 && m.withOutcome < MIN_OUTCOMES && (
                            <span
                              className="rounded bg-yellow-100 px-1.5 py-0.5 text-[10px] font-medium text-yellow-800"
                              data-testid="insufficient-data"
                            >
                              low data
                              </span>
                          )}
                        </span>
                      </td>
                      <td className="py-2.5 pr-4 text-right">
                        {m.withOutcome > 0 ? formatPercent(m.meanError, 1) : "—"}
                      </td>
                      <td className="py-2.5 text-right">
                        {m.withOutcome > 0 ? formatPercent(m.meanBias, 1) : "—"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function StatCard({
  label,
  value,
  icon: Icon,
}: {
  label: string;
  value: string | number;
  icon: React.ComponentType<{ className?: string }>;
}) {
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
        <CardTitle className="text-sm font-medium text-muted-foreground">{label}</CardTitle>
        <Icon className="h-4 w-4 text-muted-foreground" />
      </CardHeader>
      <CardContent>
        <div className="text-2xl font-bold">{value}</div>
      </CardContent>
    </Card>
  );
}
