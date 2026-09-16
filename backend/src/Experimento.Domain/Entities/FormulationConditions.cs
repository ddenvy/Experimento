namespace Experimento.Domain.Entities;

/// <summary>
/// Process conditions of a formulation version (owned by the version).
/// </summary>
public class FormulationConditions
{
    public double TemperatureCelsius { get; set; }
    public double? PressureKPa { get; set; }
    public double? PhTarget { get; set; }
    public string? Solvent { get; set; }
    public string? DeliveryTarget { get; set; }
}
