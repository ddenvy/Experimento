using Experimento.Domain.Enums;

namespace Experimento.Domain.Entities;

/// <summary>
/// An immutable snapshot of a formulation at a point in time.
/// </summary>
public class FormulationVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FormulationId { get; set; }
    public Formulation Formulation { get; set; } = null!;
    public int VersionNumber { get; set; }
    public FormulationStatus Status { get; set; } = FormulationStatus.Draft;
    public string? Notes { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<FormulationComponent> Components { get; set; } = new List<FormulationComponent>();
    public FormulationConditions Conditions { get; set; } = new();

    /// <summary>
    /// Validates the version's components. Throws InvalidOperationException when rules are broken.
    /// </summary>
    public void EnsureValid()
    {
        if (Components.Count == 0)
            throw new InvalidOperationException("A formulation version must have at least one component.");

        foreach (var component in Components)
        {
            if (component.MolarMass <= 0)
                throw new InvalidOperationException($"Molar mass of '{component.ChemicalName}' must be greater than zero.");
            if (component.Proportion is < 0 or > 1)
                throw new InvalidOperationException($"Proportion of '{component.ChemicalName}' must be in the [0, 1] range.");
        }

        var sum = Components.Sum(c => c.Proportion);
        if (sum < 0.999 || sum > 1.001)
            throw new InvalidOperationException($"Sum of proportions ({sum}) must equal 1.0 (tolerance 0.001).");
    }
}
