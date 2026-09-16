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
/// </summary>
public record IngestDocumentCommand(Guid DocumentId);
