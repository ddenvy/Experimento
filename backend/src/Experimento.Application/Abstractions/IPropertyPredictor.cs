namespace Experimento.Application.Abstractions;

/// <summary>
/// Snapshot of a formulation version passed to predictors.
/// </summary>
public record FormulationSnapshot(
    Guid VersionId,
    IReadOnlyList<ComponentSnapshot> Components,
    ConditionsSnapshot Conditions,
    string TargetPurpose);

public record ComponentSnapshot(
    string ChemicalName,
    string? CasNumber,
    string? Formula,
    double MolarMass,
    double Proportion,
    string? Role,
    string? Smiles);

public record ConditionsSnapshot(
    double TemperatureCelsius,
    double? PressureKPa,
    double? PhTarget,
    string? Solvent,
    string? DeliveryTarget);

/// <summary>
/// A single scoring factor with its contribution to the final score.
/// </summary>
public record ScoringFactor(string Name, double Contribution, string Description);

/// <summary>
/// The outcome of a property prediction.
/// </summary>
public record PredictionOutcome(
    double SuccessProbability,
    double ToxicityScore,
    double StabilityScore,
    int SideRiskLevel,
    string Summary,
    IReadOnlyList<ScoringFactor> Factors);

/// <summary>
/// Property prediction model abstraction (heuristic today, real ML later).
/// </summary>
public interface IPropertyPredictor
{
    string ModelName { get; }
    string ModelVersion { get; }
    Task<PredictionOutcome> PredictAsync(FormulationSnapshot snapshot, CancellationToken cancellationToken = default);
}
