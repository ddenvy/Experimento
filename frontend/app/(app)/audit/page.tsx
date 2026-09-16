"use client";

import { useEffect, useState } from "react";
import { api, type AuditEntryDto, type AuditIntegrityDto } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ShieldCheck, ShieldAlert, Loader2, Download } from "lucide-react";

export default function AuditPage() {
  const [entries, setEntries] = useState<AuditEntryDto[]>([]);
  const [integrity, setIntegrity] = useState<AuditIntegrityDto | null>(null);
  const [verifying, setVerifying] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);

  useEffect(() => {
    api.getAuditTrail().then(setEntries).catch(() => {});
  }, []);

  async function verify() {
    setVerifying(true);
    try {
      setIntegrity(await api.verifyAudit());
    } catch {
      setIntegrity({ isIntact: false, firstBrokenId: null });
    } finally {
      setVerifying(false);
    }
  }

  // Выгружает пакет аудита (ALCOA+ оценка, полный журнал, версии, прогоны, ревью).
  async function exportPackage() {
    setExporting(true);
    setExportError(null);
    try {
      const markdown = await api.exportAuditPackage();
      const blob = new Blob([markdown], { type: "text/markdown;charset=utf-8" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `audit-export-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, "-")}.md`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch (e) {
      setExportError(e instanceof Error ? e.message : "Failed to export audit package");
    } finally {
      setExporting(false);
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Audit Trail</h1>
        <div className="flex items-center gap-2">
          <Button variant="outline" onClick={exportPackage} disabled={exporting} data-testid="export-audit">
            {exporting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}
            Export audit package
          </Button>
          <Button onClick={verify} disabled={verifying}>
            {verifying ? <Loader2 className="h-4 w-4 animate-spin" /> : <ShieldCheck className="h-4 w-4" />}
            Verify integrity
          </Button>
        </div>
      </div>

      {exportError && <p className="text-sm text-destructive">{exportError}</p>}

      {integrity && (
        <Card className={integrity.isIntact ? "border-green-500" : "border-destructive"}>
          <CardContent className="flex items-center gap-3 p-4">
            {integrity.isIntact ? (
              <ShieldCheck className="h-5 w-5 text-green-600" />
            ) : (
              <ShieldAlert className="h-5 w-5 text-destructive" />
            )}
            <div>
              <p className="font-medium">
                {integrity.isIntact ? "Audit chain is intact." : "Audit chain is broken!"}
              </p>
              {!integrity.isIntact && integrity.firstBrokenId && (
                <p className="text-sm text-muted-foreground">First broken entry ID: {integrity.firstBrokenId}</p>
              )}
            </div>
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle>Recent entries ({entries.length})</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-muted-foreground border-b">
                  <th className="py-2 pr-4">Time</th>
                  <th className="py-2 pr-4">Action</th>
                  <th className="py-2 pr-4">Entity</th>
                  <th className="py-2">Entity ID</th>
                </tr>
              </thead>
              <tbody>
                {entries.map((e) => (
                  <tr key={e.id} className="border-b last:border-0">
                    <td className="py-2 pr-4">{new Date(e.timestampUtc).toLocaleString()}</td>
                    <td className="py-2 pr-4">{e.action}</td>
                    <td className="py-2 pr-4">{e.entityType}</td>
                    <td className="py-2 font-mono text-xs">{e.entityId ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
