"use client";

import { useEffect, useState } from "react";
import { api, type AuditEntryDto, type AuditIntegrityDto } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ShieldCheck, ShieldAlert, Loader2 } from "lucide-react";

export default function AuditPage() {
  const [entries, setEntries] = useState<AuditEntryDto[]>([]);
  const [integrity, setIntegrity] = useState<AuditIntegrityDto | null>(null);
  const [verifying, setVerifying] = useState(false);

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

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Audit Trail</h1>
        <Button onClick={verify} disabled={verifying}>
          {verifying ? <Loader2 className="h-4 w-4 animate-spin" /> : <ShieldCheck className="h-4 w-4" />}
          Verify integrity
        </Button>
      </div>

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
