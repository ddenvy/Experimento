namespace Experimento.Infrastructure.Predictions;

/// <summary>
/// Deterministic rule-based property predictor (model: rule-based-v1).
/// Produces explainable scoring factors that feed the rationale generator.
/// </summary>
public class RuleBasedPropertyPredictor : IPropertyPredictor
{
    public string ModelName => "rule-based";
    public string ModelVersion => "v1";

    private static readonly HashSet<string> Toxicophores = new(StringComparer.OrdinalIgnoreCase)
    {
        "nitro", "azide", "cyanide", "arsenic", "mercury", "lead", "cadmium", "phosgene", "hydrazine"
    };

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

        // Factor 4: toxicophore presence
        var hasToxicophore = snapshot.Components.Any(c =>
            Toxicophores.Any(t => (c.ChemicalName + " " + c.Formula + " " + c.Role).Contains(t, StringComparison.OrdinalIgnoreCase)));
        var toxicScore = hasToxicophore ? 0.3 : 0.8;
        factors.Add(new ScoringFactor("ToxicophorePresence", (hasToxicophore ? -0.4 : 0.2),
            hasToxicophore ? "Known toxicophores detected in components." : "No known toxicophores detected."));

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
