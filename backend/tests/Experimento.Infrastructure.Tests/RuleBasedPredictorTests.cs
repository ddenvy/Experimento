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
            new("Water", "7732-18-5", "H2O", 18.0, 0.8, "solvent", null),
            new("Sucrose", "57-50-1", "C12H22O11", 342.3, 0.2, "excipient",
                "C(C1C(C(C(C(O1)OC2(C(C(C(O2)CO)O)O)CO)O)O)O)O")
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
    public async Task PredictAsync_StructuralHeavyMetalPenalized()
    {
        var toxic = new FormulationSnapshot(
            Guid.NewGuid(),
            new List<ComponentSnapshot>
            {
                new("Lead acetate", "301-04-2", "Pb", 325.0, 0.5, "api", null),
                new("Water", "7732-18-5", "H2O", 18.0, 0.5, "solvent", null)
            },
            new ConditionsSnapshot(25.0, 101.3, 7.0, "water", null),
            "Toxic test");

        var outcome = await _predictor.PredictAsync(toxic);
        Assert.Equal(2, outcome.SideRiskLevel); // High
        Assert.Contains(outcome.Factors, f => f.Name == "StructuralHazards" && f.Contribution < 0);
    }

    [Fact]
    public async Task PredictAsync_AspirinNoFalseStructuralHazard()
    {
        // Аспирин ароматичен и содержит карбонилы, но ни одного из наших маркеров опасности.
        var aspirin = new FormulationSnapshot(
            Guid.NewGuid(),
            new List<ComponentSnapshot>
            {
                new("Aspirin", "50-78-2", "C9H8O4", 180.16, 1.0, "Active",
                    "CC(=O)Oc1ccccc1C(=O)O")
            },
            new ConditionsSnapshot(25.0, 101.3, 7.0, null, null),
            "Analgesic");

        var outcome = await _predictor.PredictAsync(aspirin);
        var factor = Assert.Single(outcome.Factors, f => f.Name == "StructuralHazards");
        Assert.True(factor.Contribution > 0);
    }

    [Fact]
    public async Task PredictAsync_NitroGroupPenalized()
    {
        var nitromethane = new FormulationSnapshot(
            Guid.NewGuid(),
            new List<ComponentSnapshot>
            {
                new("Nitromethane", "75-52-5", "CH3NO2", 61.04, 1.0, "solvent", "C[N+](=O)[O-]")
            },
            new ConditionsSnapshot(25.0, null, null, null, null),
            "Reactive solvent");

        var outcome = await _predictor.PredictAsync(nitromethane);
        var factor = Assert.Single(outcome.Factors, f => f.Name == "StructuralHazards");
        Assert.True(factor.Contribution < 0);
        Assert.Contains("nitro group", factor.Description);
    }

    [Fact]
    public async Task PredictAsync_MissingStabilizerForDeliveryPenalized()
    {
        var noStabilizer = new FormulationSnapshot(
            Guid.NewGuid(),
            new List<ComponentSnapshot>
            {
                new("ApiX", null, "C6H12O6", 180.0, 0.5, "api", null),
                new("Water", "7732-18-5", "H2O", 18.0, 0.5, "solvent", null)
            },
            new ConditionsSnapshot(25.0, 101.3, 7.0, "water", "intravenous"),
            "IV delivery");

        var outcome = await _predictor.PredictAsync(noStabilizer);
        var factor = Assert.Single(outcome.Factors, f => f.Name == "StabilizerForDelivery");
        Assert.True(factor.Contribution < 0);
    }
}
