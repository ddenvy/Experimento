using Experimento.Domain.Enums;

namespace Experimento.Domain.Entities;

/// <summary>
/// A queued prediction job for a formulation version.
/// </summary>
public class PredictionJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VersionId { get; set; }
    public FormulationVersion Version { get; set; } = null!;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int Progress { get; set; }
    public string? Stage { get; set; }
    public Guid RequestedBy { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public PredictionResult? Result { get; set; }
}
