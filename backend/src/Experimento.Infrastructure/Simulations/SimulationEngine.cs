using System.Text.Json;

namespace Experimento.Infrastructure.Simulations;

/// <summary>
/// Stress-test simulation engine. Runs parameter variations through the predictor
/// with a deterministic seed for reproducibility.
/// </summary>
public class SimulationEngine
{
    private readonly IPropertyPredictor _predictor;
    private readonly IJobNotifier _notifier;
    public SimulationEngine(IPropertyPredictor predictor, IJobNotifier notifier)
    {
        _predictor = predictor;
        _notifier = notifier;
    }

    public async Task<SimulationRunResult> RunAsync(Guid jobId, FormulationSnapshot baseSnapshot,
        string configJson, CancellationToken ct = default)
    {
        var config = JsonSerializer.Deserialize<SimulationConfig>(configJson) ?? new SimulationConfig();
        var random = new Random(config.Seed);

        var candidates = new List<SimulationCandidateResult>();
        var total = config.Iterations;

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var varied = ApplyVariations(baseSnapshot, config, random);
            var outcome = await _predictor.PredictAsync(varied, ct);
            var score = config.TargetMetric switch
            {
                "stability" => outcome.StabilityScore,
                "toxicity" => 1 - outcome.ToxicityScore,
                _ => outcome.SuccessProbability
            };
            candidates.Add(new SimulationCandidateResult(
                JsonSerializer.Serialize(ToParams(varied, baseSnapshot)),
                outcome.SuccessProbability, score));

            if ((i + 1) % Math.Max(1, total / 10) == 0)
                await _notifier.PublishProgressAsync("simulation", jobId, (i + 1) * 100 / total, $"Iteration {i + 1}/{total}", ct);
        }

        var ranked = candidates.OrderByDescending(c => c.Score).ToList();
        if (ranked.Count == 0)
            throw new InvalidOperationException("Simulation produced no candidates; iterations must be at least 1.");

        for (int i = 0; i < ranked.Count; i++)
            ranked[i] = ranked[i] with { Rank = i + 1 };

        var best = ranked.First();
        var summary = $"Explored {total} variations. Best candidate achieves success probability {best.SuccessProbability:P1} " +
                      $"with score {best.Score:F3}. Top parameter: {best.ParametersJson}.";

        return new SimulationRunResult(ranked, best, summary, total);
    }

    private static FormulationSnapshot ApplyVariations(FormulationSnapshot snapshot, SimulationConfig config, Random random)
    {
        var components = snapshot.Components.Select(c =>
        {
            if (config.VaryConcentrations)
            {
                var delta = (random.NextDouble() * 2 - 1) * 0.05; // ±5%
                var newProp = Math.Clamp(c.Proportion + delta, 0.01, 0.99);
                return c with { Proportion = newProp };
            }
            return c;
        }).ToList();

        // Renormalize proportions to sum 1.0
        var sum = components.Sum(c => c.Proportion);
        components = components.Select(c => c with { Proportion = c.Proportion / sum }).ToList();

        var conditions = snapshot.Conditions;
        if (config.VaryTemperature)
        {
            var delta = (random.NextDouble() * 2 - 1) * 15; // ±15°C
            conditions = conditions with { TemperatureCelsius = Math.Max(0, conditions.TemperatureCelsius + delta) };
        }
        if (config.VaryPh && conditions.PhTarget.HasValue)
        {
            var delta = (random.NextDouble() * 2 - 1) * 1.0; // ±1 pH
            conditions = conditions with { PhTarget = Math.Clamp(conditions.PhTarget.Value + delta, 0, 14) };
        }

        return snapshot with { Components = components, Conditions = conditions };
    }

    private static Dictionary<string, object> ToParams(FormulationSnapshot varied, FormulationSnapshot baseline)
    {
        var parameters = new Dictionary<string, object>();
        foreach (var (v, b) in varied.Components.Zip(baseline.Components, (v, b) => (v, b)))
        {
            if (Math.Abs(v.Proportion - b.Proportion) > 1e-6)
                parameters[$"{v.ChemicalName}.proportion"] = Math.Round(v.Proportion, 4);
        }
        if (Math.Abs(varied.Conditions.TemperatureCelsius - baseline.Conditions.TemperatureCelsius) > 1e-6)
            parameters["temperatureCelsius"] = Math.Round(varied.Conditions.TemperatureCelsius, 2);
        if (varied.Conditions.PhTarget.HasValue && baseline.Conditions.PhTarget.HasValue &&
            Math.Abs(varied.Conditions.PhTarget.Value - baseline.Conditions.PhTarget.Value) > 1e-6)
            parameters["phTarget"] = Math.Round(varied.Conditions.PhTarget.Value, 2);
        return parameters;
    }
}

public record SimulationConfig(
    bool VaryConcentrations = true,
    bool VaryTemperature = true,
    bool VaryPh = false,
    int Iterations = 100,
    int Seed = 42,
    string TargetMetric = "success");

public record SimulationCandidateResult(string ParametersJson, double SuccessProbability, double Score, int Rank = 0);

public record SimulationRunResult(
    IReadOnlyList<SimulationCandidateResult> RankedCandidates,
    SimulationCandidateResult Best,
    string Summary,
    int IterationsExecuted);
