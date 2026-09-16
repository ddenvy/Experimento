// Клиент SignalR для подписки на прогресс фоновых задач (prediction/simulation).
import { HubConnection, HubConnectionBuilder, HubConnectionState } from "@microsoft/signalr";
import { getAccessToken, getHubUrl } from "./api";

export interface JobEventHandlers {
  onProgress?: (progress: number, stage: string | null) => void;
  onCompleted?: () => void;
  onFaulted?: (error: string) => void;
}

export interface JobProgressEvent {
  jobType: string;
  jobId: string;
  progress: number;
  stage: string | null;
}

export interface JobFaultedEvent {
  jobType: string;
  jobId: string;
  error: string;
}

// Подключается к хабу и подписывается на группу конкретной задачи.
// Возвращает активное соединение; вызывающий обязан остановить его через stopJobConnection.
export async function subscribeToJob(
  jobType: "prediction" | "simulation",
  jobId: string,
  handlers: JobEventHandlers
): Promise<HubConnection> {
  const connection = new HubConnectionBuilder()
    .withUrl(getHubUrl(), {
      accessTokenFactory: () => getAccessToken() ?? "",
    })
    .withAutomaticReconnect()
    .build();

  connection.on("progress", (event: JobProgressEvent) => {
    handlers.onProgress?.(event.progress, event.stage);
  });
  connection.on("completed", handlers.onCompleted ?? (() => undefined));
  connection.on("faulted", (event: JobFaultedEvent) => {
    handlers.onFaulted?.(event.error);
  });

  await connection.start();
  await connection.invoke("SubscribeToJob", jobType, jobId);
  return connection;
}

export async function stopJobConnection(connection: HubConnection | null): Promise<void> {
  if (connection && connection.state !== HubConnectionState.Disconnected) {
    await connection.stop();
  }
}
