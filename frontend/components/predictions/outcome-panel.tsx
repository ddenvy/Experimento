"use client";

import { useState } from "react";
import { api, type PredictionResultDto } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { formatDateTime } from "@/lib/format";
import { Loader2 } from "lucide-react";

const DECISIONS = ["Approved", "NeedsRevision", "Rejected"] as const;
const RISK_LEVELS = ["Low", "Medium", "High"] as const;

const decisionStyle: Record<string, string> = {
  Approved: "bg-green-100 text-green-800",
  NeedsRevision: "bg-yellow-100 text-yellow-800",
  Rejected: "bg-red-100 text-red-800",
};

interface StoredMetrics {
  toxicity?: number;
  stability?: number;
  sideRiskLevel?: string;
}

function parseMetrics(json: string): StoredMetrics {
  try {
    const v: unknown = JSON.parse(json);
    return v && typeof v === "object" ? (v as StoredMetrics) : {};
  } catch {
    return {};
  }
}

/**
 * Панель Human-in-the-loop: ревью решения модели и лабораторный исход эксперимента.
 * После любой мутации вызывает onChanged — родитель перезагружает результат и историю.
 */
export function OutcomePanel({
  result,
  onChanged,
}: {
  result: PredictionResultDto;
  onChanged: () => Promise<void>;
}) {
  // --- Review form ---
  const [decision, setDecision] = useState<string>("Approved");
  const [comment, setComment] = useState("");
  const [savingReview, setSavingReview] = useState(false);
  const [reviewError, setReviewError] = useState<string | null>(null);

  // --- Outcome form (префилл из ранее сохранённого исхода) ---
  const stored = parseMetrics(result.outcome?.actualMetricsJson ?? "{}");
  const [actualSuccess, setActualSuccess] = useState<boolean>(result.outcome?.actualSuccess ?? true);
  const [toxicity, setToxicity] = useState(stored.toxicity?.toString() ?? "");
  const [stability, setStability] = useState(stored.stability?.toString() ?? "");
  const [sideRiskLevel, setSideRiskLevel] = useState(stored.sideRiskLevel ?? "");
  const [notes, setNotes] = useState(result.outcome?.notes ?? "");
  const [savingOutcome, setSavingOutcome] = useState(false);
  const [outcomeError, setOutcomeError] = useState<string | null>(null);

  async function submitReview() {
    setSavingReview(true);
    setReviewError(null);
    try {
      await api.submitReview(result.id, { decision, comment: comment || undefined });
      setComment("");
      await onChanged();
    } catch (e) {
      setReviewError(e instanceof Error ? e.message : "Failed to submit review");
    } finally {
      setSavingReview(false);
    }
  }

  async function saveOutcome() {
    const metrics: StoredMetrics = {};
    const t = parseFloat(toxicity);
    const s = parseFloat(stability);
    if (!Number.isNaN(t)) metrics.toxicity = Math.min(1, Math.max(0, t));
    if (!Number.isNaN(s)) metrics.stability = Math.min(1, Math.max(0, s));
    if (sideRiskLevel) metrics.sideRiskLevel = sideRiskLevel;

    setSavingOutcome(true);
    setOutcomeError(null);
    try {
      await api.recordOutcome(result.id, {
        actualSuccess,
        actualMetricsJson: JSON.stringify(metrics),
        notes: notes || undefined,
      });
      await onChanged();
    } catch (e) {
      setOutcomeError(e instanceof Error ? e.message : "Failed to save lab outcome");
    } finally {
      setSavingOutcome(false);
    }
  }

  return (
    <div className="space-y-4 border-t pt-4">
      {/* Human review */}
      <section className="space-y-3" aria-label="Prediction review">
        <h4 className="text-sm font-medium">Review decision</h4>
        {result.reviews.length > 0 && (
          <ul className="space-y-1.5" data-testid="review-list">
            {result.reviews.map((r) => (
              <li key={r.id} className="flex items-center gap-2 text-xs text-muted-foreground">
                <span className={`rounded px-1.5 py-0.5 font-medium ${decisionStyle[r.decision] ?? "bg-muted"}`}>
                  {r.decision}
                </span>
                {r.comment && <span className="truncate">{r.comment}</span>}
                <span className="ml-auto shrink-0">{formatDateTime(r.createdAtUtc)}</span>
              </li>
            ))}
          </ul>
        )}
        <div className="flex flex-wrap items-center gap-2">
          <select
            aria-label="Review decision"
            className="h-10 rounded-md border border-input bg-background px-3 text-sm"
            value={decision}
            onChange={(e) => setDecision(e.target.value)}
          >
            {DECISIONS.map((d) => (
              <option key={d} value={d}>
                {d}
              </option>
            ))}
          </select>
          <Input
            aria-label="Review comment"
            className="max-w-xs flex-1"
            placeholder="Comment (optional)"
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            maxLength={1000}
          />
          <Button
            type="button"
            variant="outline"
            size="sm"
            data-testid="submit-review"
            onClick={submitReview}
            disabled={savingReview}
          >
            {savingReview && <Loader2 className="h-4 w-4 animate-spin" />}
            Submit review
          </Button>
        </div>
        {reviewError && <p className="text-sm text-destructive">{reviewError}</p>}
      </section>

      {/* Lab outcome */}
      <section className="space-y-3 rounded-md border p-3" aria-label="Lab outcome">
        <div className="flex items-center justify-between">
          <h4 className="text-sm font-medium">Lab outcome</h4>
          {result.outcome && (
            <span className="text-xs text-muted-foreground">
              Recorded {formatDateTime(result.outcome.recordedAtUtc)}
            </span>
          )}
        </div>
        <div className="flex flex-wrap gap-3">
          <label className="flex items-center gap-1.5 text-sm">
            <span>Actual result</span>
            <select
              aria-label="Actual lab result"
              className="h-10 rounded-md border border-input bg-background px-3 text-sm"
              value={actualSuccess ? "yes" : "no"}
              onChange={(e) => setActualSuccess(e.target.value === "yes")}
            >
              <option value="yes">Success</option>
              <option value="no">Failure</option>
            </select>
          </label>
          <label className="flex items-center gap-1.5 text-sm">
            <span>Toxicity</span>
            <Input
              aria-label="Observed toxicity"
              type="number"
              step="0.05"
              min="0"
              max="1"
              className="h-10 w-24"
              placeholder="0–1"
              value={toxicity}
              onChange={(e) => setToxicity(e.target.value)}
            />
          </label>
          <label className="flex items-center gap-1.5 text-sm">
            <span>Stability</span>
            <Input
              aria-label="Observed stability"
              type="number"
              step="0.05"
              min="0"
              max="1"
              className="h-10 w-24"
              placeholder="0–1"
              value={stability}
              onChange={(e) => setStability(e.target.value)}
            />
          </label>
          <label className="flex items-center gap-1.5 text-sm">
            <span>Side risk</span>
            <select
              aria-label="Observed side risk"
              className="h-10 rounded-md border border-input bg-background px-3 text-sm"
              value={sideRiskLevel}
              onChange={(e) => setSideRiskLevel(e.target.value)}
            >
              <option value="">—</option>
              {RISK_LEVELS.map((r) => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
            </select>
          </label>
        </div>
        <Input
          aria-label="Lab notes"
          placeholder="Lab notes (optional)"
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          maxLength={2000}
        />
        <div className="flex items-center gap-2">
          <Button
            type="button"
            size="sm"
            data-testid="save-outcome"
            onClick={saveOutcome}
            disabled={savingOutcome}
          >
            {savingOutcome && <Loader2 className="h-4 w-4 animate-spin" />}
            {result.outcome ? "Update outcome" : "Save outcome"}
          </Button>
          {outcomeError && <p className="text-sm text-destructive">{outcomeError}</p>}
        </div>
      </section>
    </div>
  );
}
