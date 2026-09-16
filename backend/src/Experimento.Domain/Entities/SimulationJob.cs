using Experimento.Domain.Enums;

namespace Experimento.Domain.Entities;

/// <summary>
/// A stress-test simulation job that explores parameter variations.
/// </summary>
public class SimulationJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VersionId { get; set; }
    public FormulationVersion Version { get; set; } = null!;
    public string ConfigJson { get; set; } = "{}";
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int Progress { get; set; }
    public Guid RequestedBy { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public SimulationResult? Result { get; set; }
}
