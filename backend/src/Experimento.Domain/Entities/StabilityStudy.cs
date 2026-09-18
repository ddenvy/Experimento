namespace Experimento.Domain.Entities;

/// <summary>
/// A stability study for a formulation version: measured content of the active substance
/// over time at one or more storage temperatures.
/// </summary>
public class StabilityStudy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VersionId { get; set; }
    public FormulationVersion Version { get; set; } = null!;
    public string? Notes { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<StabilityPoint> Points { get; set; } = new List<StabilityPoint>();

    /// <summary>
    /// Validates the study's measurement points. Throws InvalidOperationException when rules are broken.
    /// </summary>
    public void EnsureValid()
    {
        if (Points.Count == 0)
            throw new InvalidOperationException("A stability study must have at least one measurement point.");

        foreach (var point in Points)
        {
            if (point.TimeDays < 0)
                throw new InvalidOperationException("Measurement time must not be negative.");
            if (point.AssayPercent is <= 0 or > 100)
                throw new InvalidOperationException(
                    $"Assay of {point.AssayPercent}% is outside the (0, 100] range.");
        }
    }
}
