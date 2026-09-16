import { cn } from "@/lib/utils";

const styles: Record<string, string> = {
  Completed: "border-green-500 text-green-700 bg-green-50 dark:bg-green-950/30",
  Failed: "border-red-500 text-red-700 bg-red-50 dark:bg-red-950/30",
  Running: "border-blue-400 text-blue-700 bg-blue-50 dark:bg-blue-950/30",
  Pending: "border-yellow-400 text-yellow-700 bg-yellow-50 dark:bg-yellow-950/30",
};

// Единый бейдж статуса job'а для истории прогнозов и симуляций.
export function RunStatusBadge({ status }: { status: string }) {
  return (
    <span
      className={cn(
        "inline-flex w-20 shrink-0 justify-center rounded border px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide",
        styles[status] ?? "border-input text-muted-foreground"
      )}
    >
      {status}
    </span>
  );
}
