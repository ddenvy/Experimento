namespace Experimento.Domain.Entities;

/// <summary>
/// A single candidate formulation produced by a simulation.
/// </summary>
public class SimulationCandidate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResultId { get; set; }
    public SimulationResult Result { get; set; } = null!;
    public string ParametersJson { get; set; } = "{}";
    public double SuccessProbability { get; set; }
    public double Score { get; set; }
    public int Rank { get; set; }
}
