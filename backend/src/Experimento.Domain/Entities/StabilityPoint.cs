namespace Experimento.Domain.Entities;

/// <summary>
/// A single measurement of a stability study: content of the active substance after a given
/// time at a given storage temperature. The assay is a percentage of the initial content.
/// </summary>
public class StabilityPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudyId { get; set; }
    public StabilityStudy Study { get; set; } = null!;
    public double TemperatureCelsius { get; set; }
    public double TimeDays { get; set; }
    public double AssayPercent { get; set; }
}
