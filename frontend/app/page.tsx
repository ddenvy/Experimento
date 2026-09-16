import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { FlaskConical, Brain, Activity, ShieldCheck } from "lucide-react";

export default function DashboardPage() {
  const stats = [
    { label: "Formulations", value: "12", icon: FlaskConical, hint: "+3 this week" },
    { label: "Predictions", value: "47", icon: Brain, hint: "avg. 78% success" },
    { label: "Simulations", value: "23", icon: Activity, hint: "all completed" },
    { label: "Audit Integrity", value: "100%", icon: ShieldCheck, hint: "chain intact" },
  ];

  return (
    <div className="space-y-8">
      <div>
        <h1 className="text-3xl font-bold tracking-tight">Dashboard</h1>
        <p className="text-muted-foreground mt-1">
          Design formulations, predict properties, simulate outcomes — every step audited.
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {stats.map((s) => {
          const Icon = s.icon;
          return (
            <Card key={s.label}>
              <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">{s.label}</CardTitle>
                <Icon className="h-4 w-4 text-muted-foreground" />
              </CardHeader>
              <CardContent>
                <div className="text-2xl font-bold">{s.value}</div>
                <p className="text-xs text-muted-foreground mt-1">{s.hint}</p>
              </CardContent>
            </Card>
          );
        })}
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>Recent activity</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3 text-sm">
            <div className="flex justify-between">
              <span>Prediction job #a1f2… completed</span>
              <span className="text-muted-foreground">2m ago</span>
            </div>
            <div className="flex justify-between">
              <span>New formulation version v3 for Project A</span>
              <span className="text-muted-foreground">18m ago</span>
            </div>
            <div className="flex justify-between">
              <span>Audit chain verified — intact</span>
              <span className="text-muted-foreground">1h ago</span>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Compliance status</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2 text-sm">
            <div className="flex items-center gap-2">
              <ShieldCheck className="h-4 w-4 text-green-600" />
              <span>ALCOA+ audit trail active</span>
            </div>
            <div className="flex items-center gap-2">
              <ShieldCheck className="h-4 w-4 text-green-600" />
              <span>Model version recorded per prediction</span>
            </div>
            <div className="flex items-center gap-2">
              <ShieldCheck className="h-4 w-4 text-green-600" />
              <span>Explainable rationale for every score</span>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
