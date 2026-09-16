namespace Experimento.Infrastructure.Predictions;

/// <summary>
/// Deterministic rule-based property predictor (model: rule-based-v2).
/// Produces explainable scoring factors that feed the rationale generator.
/// </summary>
public class RuleBasedPropertyPredictor : IPropertyPredictor
{
    public string ModelName => "rule-based";
    public string ModelVersion => "v2";

    public Task<PredictionOutcome> PredictAsync(FormulationSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var factors = new List<ScoringFactor>();

        // Factor 1: molar mass balance
        var totalMass = snapshot.Components.Sum(c => c.MolarMass * c.Proportion);
        var massScore = Math.Clamp(totalMass / 500.0, 0, 1);
        factors.Add(new ScoringFactor("MolarMassBalance", massScore - 0.5,
            $"Average weighted molar mass is {totalMass:F1} g/mol."));

        // Factor 2: proportion uniformity (closer to uniform = more stable mix)
        var proportions = snapshot.Components.Select(c => c.Proportion).ToList();
        var variance = proportions.Count > 1
            ? proportions.Average(p => Math.Pow(p - 1.0 / proportions.Count, 2))
            : 0;
        var uniformityScore = Math.Max(0, 1 - Math.Sqrt(variance) * 5);
        factors.Add(new ScoringFactor("ProportionUniformity", uniformityScore - 0.5,
            $"Proportion variance is {variance:F4}."));

        // Factor 3: temperature stability (room temp ~25C is safest)
        var tempDelta = Math.Abs(snapshot.Conditions.TemperatureCelsius - 25);
        var tempScore = Math.Max(0, 1 - tempDelta / 100.0);
        factors.Add(new ScoringFactor("TemperatureStability", tempScore - 0.5,
            $"Temperature {snapshot.Conditions.TemperatureCelsius}°C is {tempDelta:F0}°C from ambient."));

        // Factor 4: структурный скрининг опасности по формуле и SMILES из каталога.
        // Категории невзаимоисключающие, поэтому вклады суммируются (с потолком).
        var analyses = snapshot.Components
            .Select(c => StructureAnalyzer.Analyze(c.Formula, c.Smiles, $"{c.ChemicalName} {c.Role}"))
            .ToList();
        var anyHeavy = analyses.Any(a => a.HeavyMetal);
        var anyEnergetic = analyses.Any(a => a.EnergeticGroup);
        var anyReactive = analyses.Any(a => a.ReactiveGroup);
        var anyHalogen = analyses.Any(a => a.Halogenated);

        var hazardPenalty = 0.0
                           + (anyHeavy ? 0.5 : 0)
                           + (anyEnergetic ? 0.3 : 0)
                           + (anyReactive ? 0.2 : 0)
                           + (anyHalogen ? 0.1 : 0);
        hazardPenalty = Math.Min(0.7, hazardPenalty);

        var findings = analyses.SelectMany(a => a.Findings).Distinct().ToList();
        var hazardContribution = hazardPenalty == 0 ? 0.2 : -(0.1 + hazardPenalty);
        var hazardDescription = hazardPenalty == 0
            ? "No structural hazard indicators detected from catalog formula/SMILES."
            : "Structural hazard indicators: " + string.Join("; ", findings) + ".";
        factors.Add(new ScoringFactor("StructuralHazards", hazardContribution, hazardDescription));

        // Базовый уровень «безопасности» 0.8, вычитаем подтверждённые структурные риски.
        var toxicScore = Math.Clamp(0.8 - hazardPenalty, 0.1, 0.9);

        // Factor 5: stabilizer presence for targeted delivery
        var needStabilizer = !string.IsNullOrEmpty(snapshot.Conditions.DeliveryTarget);
        var hasStabilizer = snapshot.Components.Any(c =>
            string.Equals(c.Role, "stabilizer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Role, "surfactant", StringComparison.OrdinalIgnoreCase));
        double stabilizerFactor;
        if (needStabilizer && !hasStabilizer)
            stabilizerFactor = -0.3;
        else if (needStabilizer && hasStabilizer)
            stabilizerFactor = 0.2;
        else
            stabilizerFactor = 0;
        factors.Add(new ScoringFactor("StabilizerForDelivery", stabilizerFactor,
            needStabilizer
                ? (hasStabilizer ? "Stabilizer present for targeted delivery." : "Missing stabilizer for targeted delivery.")
                : "No targeted delivery requirement."));

        // Aggregate
        var successProb = Math.Clamp(0.5 + factors.Sum(f => f.Contribution), 0, 1);
        var toxicityScore = Math.Clamp(1 - toxicScore, 0, 1);
        var stabilityScore = Math.Clamp(0.5 + (uniformityScore - 0.5) + (tempScore - 0.5), 0, 1);
        var sideRisk = (SideRiskLevel)(toxicityScore switch
        {
            < 0.33 => 0,
            < 0.66 => 1,
            _ => 2
        });

        var summary = $"Predicted synthesis success {successProb:P1}. " +
                      $"Toxicity {toxicityScore:F2}, stability {stabilityScore:F2}, side risk {sideRisk}.";

        return Task.FromResult(new PredictionOutcome(
            successProb, toxicityScore, stabilityScore, (int)sideRisk, summary, factors));
    }
}
