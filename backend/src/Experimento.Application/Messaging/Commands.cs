namespace Experimento.Application.Messaging;

/// <summary>
/// Command to execute a prediction job.
/// </summary>
public record SubmitPredictionCommand(Guid JobId);

/// <summary>
/// Command to execute a simulation (stress-test) job.
/// </summary>
public record SubmitSimulationCommand(Guid JobId);

/// <summary>
/// Command to ingest (chunk + embed) a knowledge document.
/// Контент передаётся прямо в сообщении: таблица KnowledgeChunks содержит только
/// финальные чанки с векторами (колонка vector(1536) NOT NULL).
/// </summary>
public record IngestDocumentCommand(Guid DocumentId, string Content);
