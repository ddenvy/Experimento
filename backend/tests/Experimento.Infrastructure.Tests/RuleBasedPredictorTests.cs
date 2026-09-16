using Experimento.Application.Abstractions;
using Experimento.Infrastructure.Predictions;

namespace Experimento.Infrastructure.Tests;

/// <summary>
/// Tests for the rule-based property predictor (deterministic, explainable scoring).
/// </summary>
public class RuleBasedPropertyPredictorTests
{
    private readonly RuleBasedPropertyPredictor _predictor = new();

    private static FormulationSnapshot SafeSnapshot() => new(
        Guid.NewGuid(),
        new List<ComponentSnapshot>
        {
            new("Water", "H2O", "H2O", 18.0, 0.8, "solvent"),
            new("Sucrose", null, "C12H22O11", 342.3, 0.2, "excipient")
        },
        new ConditionsSnapshot(25.0, 101.3, 7.0, "water", null),
        "Oral solution");

    [Fact]
    public async Task PredictAsync_SuccessProbabilityWithinRange()
    {
        var outcome = await _predictor.PredictAsync(SafeSnapshot());
        Assert.InRange(outcome.SuccessProbability, 0.0, 1.0);
        Assert.InRange(outcome.ToxicityScore, 0.0, 1.0);
        Assert.InRange(outcome.StabilityScore, 0.0, 1.0);
    }

    [Fact]
    public async Task PredictAsync_IsDeterministic()
    {
        var a = await _predictor.PredictAsync(SafeSnapshot());
        var b = await _predictor.PredictAsync(SafeSnapshot());
        Assert.Equal(a.SuccessProbability, b.SuccessProbability);
        Assert.Equal(a.Summary, b.Summary);
    }

    [Fact]
    public async Task PredictAsync_ToxicophoreIncreasesSideRisk()
    {
        var toxic = new FormulationSnapshot(
            Guid.NewGuid(),
            new List<ComponentSnapshot>
            {
                new("Lead acetate", null, "Pb", 325.0, 0.5, "api"),
                new("Water", null, "H2O", 18.0, 0.5, "solvent")
            },
            new ConditionsSnapshot(25.0, 101.3, 7.0, "water", null),
            "Toxic test");

        var outcome = await _predictor.PredictAsync(toxic);
        Assert.Equal(2, outcome.SideRiskLevel); // High
        Assert.Contains(outcome.Factors, f => f.Name == "ToxicophorePresence" && f.Contribution < 0);
    }

    [Fact]
    public async Task PredictAsync_MissingStabilizerForDeliveryPenalized()
    {
        var noStabilizer = new FormulationSnapshot(
            Guid.NewGuid(),
            new List<ComponentSnapshot>
            {
                new("ApiX", null, "C6H12O6", 180.0, 0.5, "api"),
                new("Water", null, "H2O", 18.0, 0.5, "solvent")
            },
            new ConditionsSnapshot(25.0, 101.3, 7.0, "water", "intravenous"),
            "IV delivery");

        var outcome = await _predictor.PredictAsync(noStabilizer);
        var factor = Assert.Single(outcome.Factors, f => f.Name == "StabilizerForDelivery");
        Assert.True(factor.Contribution < 0);
    }
}
