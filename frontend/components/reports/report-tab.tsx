"use client";

import { useRef, useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { api, type FormulationVersionDto } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { FileText, Loader2, Download, Printer } from "lucide-react";

// Минимальная типографика для отрендеренного Markdown (без @tailwindcss/typography).
const mdComponents = {
  h1: (p: React.ComponentPropsWithoutRef<"h1">) => (
    <h1 className="mb-3 mt-6 text-2xl font-bold first:mt-0" {...p} />
  ),
  h2: (p: React.ComponentPropsWithoutRef<"h2">) => (
    <h2 className="mb-2 mt-6 border-b pb-1 text-lg font-semibold first:mt-0" {...p} />
  ),
  h3: (p: React.ComponentPropsWithoutRef<"h3">) => (
    <h3 className="mb-1 mt-4 text-base font-semibold" {...p} />
  ),
  h4: (p: React.ComponentPropsWithoutRef<"h4">) => (
    <h4 className="mb-1 mt-3 text-sm font-semibold" {...p} />
  ),
  p: (p: React.ComponentPropsWithoutRef<"p">) => <p className="my-2 text-sm leading-6" {...p} />,
  ul: (p: React.ComponentPropsWithoutRef<"ul">) => (
    <ul className="my-2 list-disc pl-5 text-sm leading-6" {...p} />
  ),
  li: (p: React.ComponentPropsWithoutRef<"li">) => <li className="my-0.5" {...p} />,
  strong: (p: React.ComponentPropsWithoutRef<"strong">) => (
    <strong className="font-semibold" {...p} />
  ),
  code: (p: React.ComponentPropsWithoutRef<"code">) => (
    <code className="rounded bg-muted px-1 py-0.5 text-xs" {...p} />
  ),
  table: (p: React.ComponentPropsWithoutRef<"table">) => (
    <table className="w-full border-collapse text-xs" {...p} />
  ),
  th: (p: React.ComponentPropsWithoutRef<"th">) => (
    <th className="border px-2 py-1 text-left font-semibold" {...p} />
  ),
  td: (p: React.ComponentPropsWithoutRef<"td">) => (
    <td className="border px-2 py-1 align-top" {...p} />
  ),
} as const;

// Стили печатного документа (скрытый iframe, без chrome приложения).
const printCss = `
  body { font-family: -apple-system, "Segoe UI", Roboto, sans-serif; margin: 24px; color: #111; }
  h1 { font-size: 20px; margin: 0 0 12px; }
  h2 { font-size: 15px; margin: 18px 0 6px; border-bottom: 1px solid #999; padding-bottom: 2px; }
  h3 { font-size: 13px; margin: 12px 0 4px; }
  p, li { font-size: 12px; line-height: 1.5; }
  table { border-collapse: collapse; width: 100%; font-size: 11px; margin: 8px 0; }
  th, td { border: 1px solid #666; padding: 3px 6px; text-align: left; vertical-align: top; }
  code { background: #eee; padding: 1px 3px; }
`;

export function ReportTab({
  versions,
  initialVersionId,
}: {
  versions: FormulationVersionDto[];
  initialVersionId?: string;
}) {
  const lastVersionId = versions[versions.length - 1]?.id ?? "";
  const [versionId, setVersionId] = useState(initialVersionId ?? lastVersionId);
  const [markdown, setMarkdown] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const contentRef = useRef<HTMLDivElement>(null);

  // Реагируем на внешнюю передачу версии (?version= / кнопки потока):
  // корректировка состояния во время рендера, без эффекта.
  const [prevInitialVersionId, setPrevInitialVersionId] = useState(initialVersionId);
  if (initialVersionId && initialVersionId !== prevInitialVersionId) {
    setPrevInitialVersionId(initialVersionId);
    setVersionId(initialVersionId);
    setMarkdown(null);
    setError(null);
  }

  function handleVersionChange(next: string) {
    setVersionId(next);
    // Показанный отчёт относится к прежней версии — сбрасываем.
    setMarkdown(null);
    setError(null);
  }

  async function generateReport() {
    if (!versionId) return;
    setLoading(true);
    setError(null);
    try {
      setMarkdown(await api.getVersionReport(versionId));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to generate report");
    } finally {
      setLoading(false);
    }
  }

  function downloadMarkdown() {
    if (!markdown) return;
    const version = versions.find((v) => v.id === versionId);
    const blob = new Blob([markdown], { type: "text/markdown;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = version ? `formulation-v${version.versionNumber}-report.md` : "formulation-report.md";
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  }

  function printReport() {
    if (!contentRef.current) return;
    const iframe = document.createElement("iframe");
    iframe.setAttribute("aria-hidden", "true");
    iframe.style.position = "fixed";
    iframe.style.right = "0";
    iframe.style.bottom = "0";
    iframe.style.width = "0";
    iframe.style.height = "0";
    iframe.style.border = "0";
    document.body.appendChild(iframe);

    const win = iframe.contentWindow;
    const doc = win?.document;
    if (!win || !doc) {
      iframe.remove();
      return;
    }
    doc.open();
    doc.write(
      `<!doctype html><html><head><meta charset="utf-8"><title>Formulation report</title><style>${printCss}</style></head><body>${contentRef.current.innerHTML}</body></html>`
    );
    doc.close();

    // Диалог печати блокирует JS — убираем iframe после его закрытия.
    win.onafterprint = () => iframe.remove();
    win.onload = () => {
      win.focus();
      win.print();
    };
    // Подстраховка, если событие afterprint не придёт.
    window.setTimeout(() => iframe.remove(), 120_000);
  }

  if (versions.length === 0) {
    return (
      <Card className="border-dashed">
        <CardContent className="py-4 text-sm text-muted-foreground">
          Create a version to generate its inspection report.
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <FileText className="h-4 w-4" /> Version report
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="flex flex-wrap items-center gap-2">
            <select
              aria-label="Select version"
              className="h-10 rounded-md border border-input bg-background px-3 text-sm"
              value={versionId}
              onChange={(e) => handleVersionChange(e.target.value)}
            >
              {versions.map((v) => (
                <option key={v.id} value={v.id}>
                  v{v.versionNumber}
                </option>
              ))}
            </select>
            <Button type="button" onClick={generateReport} disabled={loading || !versionId} data-testid="generate-report">
              {loading && <Loader2 className="h-4 w-4 animate-spin" />}
              Generate report
            </Button>
            {markdown && (
              <>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={downloadMarkdown}
                  data-testid="download-report"
                >
                  <Download className="h-4 w-4" /> Download .md
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={printReport}
                  data-testid="print-report"
                >
                  <Printer className="h-4 w-4" /> Print / PDF
                </Button>
              </>
            )}
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
          {!markdown && !loading && !error && (
            <p className="text-sm text-muted-foreground">
              Report includes composition, conditions, predictions with reviews and lab outcomes,
              simulations and audit trail.
            </p>
          )}
        </CardContent>
      </Card>

      {markdown && (
        <Card>
          <CardContent className="pt-6">
            {/* Широкие GFM-таблицы скроллятся внутри контейнера, а не двигают всю страницу. */}
            <div className="overflow-x-auto" data-testid="report-content" ref={contentRef}>
              <ReactMarkdown remarkPlugins={[remarkGfm]} components={mdComponents}>
                {markdown}
              </ReactMarkdown>
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
