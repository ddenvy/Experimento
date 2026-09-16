// Обёртка над SignalR-подпиской на фоновый job: единый await завершения,
// защита от гонки (job мог завершиться до подписки) и отменa при размонтировании.
import type { HubConnection } from "@microsoft/signalr";
import { api, type JobDto } from "./api";
import { subscribeToJob, stopJobConnection } from "./realtime";

export interface JobTrackResult {
  ok: boolean;
  error?: string;
}

export interface JobTrackHandle {
  done: Promise<JobTrackResult>;
  cancel: () => void;
}

export function trackJob(
  kind: "prediction" | "simulation",
  jobId: string,
  onProgress: (progress: number) => void
): JobTrackHandle {
  let connection: HubConnection | null = null;
  let settled = false;

  const finish = (resolve: (r: JobTrackResult) => void, result: JobTrackResult) => {
    if (settled) return;
    settled = true;
    void stopJobConnection(connection);
    resolve(result);
  };

  const done = new Promise<JobTrackResult>((resolve) => {
    subscribeToJob(kind, jobId, {
      onProgress: (p) => onProgress(p),
      onCompleted: () => finish(resolve, { ok: true }),
      onFaulted: (message) => finish(resolve, { ok: false, error: message }),
    })
      .then(async (conn) => {
        connection = conn;
        // Защита от гонки: задача могла завершиться до установки соединения.
        const current: JobDto =
          kind === "prediction"
            ? await api.getPredictionJob(jobId)
            : await api.getSimulationJob(jobId);
        if (current.status === "Completed") finish(resolve, { ok: true });
        else if (current.status === "Failed")
          finish(resolve, { ok: false, error: `${kind} failed. Please retry or contact support.` });
      })
      .catch(() =>
        finish(resolve, { ok: false, error: "Failed to subscribe to job updates." })
      );
  });

  return {
    done,
    cancel: () => {
      settled = true;
      void stopJobConnection(connection);
    },
  };
}
