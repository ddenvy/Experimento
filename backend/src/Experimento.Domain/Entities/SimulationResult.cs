namespace Experimento.Domain.Entities;

/// <summary>
/// The aggregated result of a simulation run.
/// </summary>
public class SimulationResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public SimulationJob Job { get; set; } = null!;
    public Guid? BestCandidateId { get; set; }
    public SimulationCandidate? BestCandidate { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int IterationsExecuted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<SimulationCandidate> Candidates { get; set; } = new List<SimulationCandidate>();
}
