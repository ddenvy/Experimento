namespace Experimento.Domain.Entities;

/// <summary>
/// A formulation aggregate that owns its versions.
/// </summary>
public class Formulation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string TargetPurpose { get; set; } = string.Empty;
    public int CurrentVersionNumber { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<FormulationVersion> Versions { get; set; } = new List<FormulationVersion>();

    /// <summary>
    /// Returns the next version number for this formulation.
    /// </summary>
    public int NextVersionNumber()
    {
        return CurrentVersionNumber + 1;
    }
}
