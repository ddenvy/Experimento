namespace Experimento.Application.Messaging;

/// <summary>
/// Progress update for an async job.
/// </summary>
public record JobProgressEvent(string JobType, Guid JobId, int Progress, string? Stage);

/// <summary>
/// Raised when a prediction job completes.
/// </summary>
public record PredictionCompletedEvent(Guid JobId, Guid ResultId);

/// <summary>
/// Raised when a prediction job fails.
/// </summary>
public record PredictionFaultedEvent(Guid JobId, string Error);

/// <summary>
/// Raised when a simulation job completes.
/// </summary>
public record SimulationCompletedEvent(Guid JobId, Guid ResultId);

/// <summary>
/// Raised when a simulation job fails.
/// </summary>
public record SimulationFaultedEvent(Guid JobId, string Error);
