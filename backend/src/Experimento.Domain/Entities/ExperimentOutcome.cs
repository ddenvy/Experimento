namespace Experimento.Domain.Entities;

/// <summary>
/// Actual laboratory outcome recorded after a real experiment.
/// </summary>
public class ExperimentOutcome
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResultId { get; set; }
    public PredictionResult Result { get; set; } = null!;
    public bool ActualSuccess { get; set; }
    public string ActualMetricsJson { get; set; } = "{}";
    public string? Notes { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}
