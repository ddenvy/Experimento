using Experimento.Application.Features.Formulations;

namespace Experimento.Application.Tests;

/// <summary>
/// Тесты правил планирования следующего эксперимента: непроверенные лиды симуляций,
/// воспроизведение удачной версии, закрытие цикла и уверенность по калибровке модели.
/// </summary>
public class NextExperimentPlannerTests
{
    private static readonly Guid FormulationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static PlannerVersion Version(
        Guid id,
        int number,
        bool hasOutcome = false,
        bool succeeded = false,
        double? predicted = null,
        string solvent = "water",
        params (string Name, double Proportion)[] components)
        => new(
            id,
            number,
            components.Select(c => new PlannerComponent(c.Name, c.Proportion)).ToList(),
            new PlannerConditions(25, 7, solvent),
            hasOutcome,
            succeeded,
            predicted,
            predicted.HasValue ? new DateTime(2026, 1, number, 0, 0, 0, DateTimeKind.Utc) : null);

    private static PlannerLead Lead(Guid versionId, double score, double success,
        params (string Name, double Value)[] parameters)
        => new(versionId, score, success,
            parameters.Select(p => new SuggestedParameterDto(p.Name, p.Value)).ToList());

    [Fact(DisplayName = "Empty formulation yields an empty plan")]
    public void NoVersions_YieldsEmptyPlan()
    {
        var plan = NextExperimentPlanner.Build(FormulationId, [], [], limit: 5);

        Assert.Empty(plan.Recommendations);
        Assert.Equal(0, plan.VersionsTotal);
        Assert.Equal(0, plan.OutcomesRecorded);
        Assert.Null(plan.MeanCalibrationError);
    }

    [Fact(DisplayName = "A simulation lead on an untested version is recommended as a hypothesis")]
    public void UntestedLead_IsRecommendedWithLowConfidence()
    {
        var v1 = Guid.NewGuid();
        var versions = new[] { Version(v1, 1, predicted: 0.7, components: [("Aspirin", 0.6)], solvent: "water") };
        var leads = new[] { Lead(v1, 0.91, 0.88, ("Aspirin.proportion", 0.62), ("temperatureCelsius", 38.5)) };

        var plan = NextExperimentPlanner.Build(FormulationId, versions, leads, limit: 5);

        var lead = Assert.Single(plan.Recommendations, r => r.Kind == "SimulationLead");
        // Исходов нет — модель ничем не подтверждена, рекомендация остаётся гипотезой.
        Assert.Equal(NextExperimentConfidence.Low, lead.Confidence);
        Assert.Contains("No laboratory outcomes are recorded yet", lead.Rationale);
        Assert.Contains("Aspirin at 0.62", lead.Title);
        Assert.Contains("38.5 °C", lead.Title);
        Assert.Equal(v1, lead.VersionId);
        Assert.Equal(1, lead.VersionNumber);
        Assert.Equal(2, lead.SuggestedParameters.Count);
        Assert.Contains("no laboratory outcome recorded for this version", lead.Evidence);
        Assert.Equal(0, plan.OutcomesRecorded);

        // Прогноз без исхода дополнительно напоминает закрыть цикл.
        Assert.Single(plan.Recommendations, r => r.Kind == "CloseLoop");
    }

    [Fact(DisplayName = "A version with a recorded outcome is not recommended again")]
    public void TestedVersion_LeadIsSkipped()
    {
        var tested = Guid.NewGuid();
        var untested = Guid.NewGuid();
        var versions = new[]
        {
            Version(tested, 1, hasOutcome: true, succeeded: false, predicted: 0.8, components: [("Aspirin", 1.0)]),
            Version(untested, 2, predicted: 0.6, components: [("Aspirin", 1.0)]),
        };
        var leads = new[]
        {
            Lead(tested, 0.95, 0.9, ("temperatureCelsius", 40)),
            Lead(untested, 0.70, 0.65, ("temperatureCelsius", 30)),
        };

        var plan = NextExperimentPlanner.Build(FormulationId, versions, leads, limit: 5);

        var lead = Assert.Single(plan.Recommendations, r => r.Kind == "SimulationLead");
        Assert.Equal(untested, lead.VersionId);
    }

    [Fact(DisplayName = "Leads are ordered by simulation score")]
    public void Leads_AreOrderedByScore()
    {
        var low = Guid.NewGuid();
        var high = Guid.NewGuid();
        var versions = new[]
        {
            Version(low, 1, components: [("Aspirin", 1.0)]),
            Version(high, 2, components: [("Aspirin", 1.0)]),
        };
        var leads = new[]
        {
            Lead(low, 0.40, 0.30, ("temperatureCelsius", 20)),
            Lead(high, 0.90, 0.85, ("temperatureCelsius", 35)),
        };

        var plan = NextExperimentPlanner.Build(FormulationId, versions, leads, limit: 5);

        Assert.Equal([high, low], plan.Recommendations.Select(r => r.VersionId).ToList());
    }

    [Fact(DisplayName = "Confidence follows the model's calibration on recorded outcomes")]
    public void Confidence_FollowsCalibration()
    {
        // Три исхода с малой ошибкой: прогноз 0.9 → 1.0, 0.2 → 0.0, 0.8 → 1.0.
        var trusted = BuildWithOutcomes((0.9, true), (0.2, false), (0.8, true));
        Assert.Equal(NextExperimentConfidence.High, Assert.Single(trusted.Recommendations, r => r.Kind == "SimulationLead").Confidence);

        // Ошибка 0.3 на единственном исходе — середина шкалы: исход есть, но его мало.
        var shaky = BuildWithOutcomes((0.7, true));
        Assert.Equal(NextExperimentConfidence.Medium, Assert.Single(shaky.Recommendations, r => r.Kind == "SimulationLead").Confidence);

        // Ошибка 0.8: модель расходится с лабораторией, вести по ней нельзя.
        var unreliable = BuildWithOutcomes((0.2, true));
        Assert.Equal(NextExperimentConfidence.Low, Assert.Single(unreliable.Recommendations, r => r.Kind == "SimulationLead").Confidence);
        Assert.Contains("mean error", unreliable.Recommendations[0].Rationale);
    }

    [Fact(DisplayName = "A successful version is proposed for reproduction with its change described")]
    public void SuccessfulVersion_IsProposedForReproduction()
    {
        var versions = new[]
        {
            Version(Guid.NewGuid(), 1, hasOutcome: true, succeeded: false, predicted: 0.4,
                components: [("Aspirin", 0.6), ("Sodium chloride", 0.4)]),
            Version(Guid.NewGuid(), 2, hasOutcome: true, succeeded: true, predicted: 0.8,
                solvent: "ethanol", components: [("Aspirin", 0.75), ("Sodium chloride", 0.25)]),
        };

        var plan = NextExperimentPlanner.Build(FormulationId, versions, [], limit: 5);

        var repeat = Assert.Single(plan.Recommendations);
        Assert.Equal("RepeatSuccess", repeat.Kind);
        Assert.Equal(NextExperimentConfidence.High, repeat.Confidence);
        Assert.Equal(2, repeat.VersionNumber);
        Assert.Contains(repeat.Evidence, e => e.Contains("succeeded in the laboratory"));
        Assert.Contains(repeat.Evidence, e => e.Contains("Aspirin 0.6 → 0.75"));
        Assert.Contains(repeat.Evidence, e => e.Contains("solvent water → ethanol"));
    }

    [Fact(DisplayName = "A prediction without an outcome asks to close the loop")]
    public void PredictionWithoutOutcome_AsksToCloseTheLoop()
    {
        var versions = new[]
        {
            Version(Guid.NewGuid(), 1, hasOutcome: true, succeeded: true, predicted: 0.9, components: [("Aspirin", 1.0)]),
            Version(Guid.NewGuid(), 2, predicted: 0.55, components: [("Aspirin", 1.0)]),
        };

        var plan = NextExperimentPlanner.Build(FormulationId, versions, [], limit: 5);

        var close = Assert.Single(plan.Recommendations, r => r.Kind == "CloseLoop");
        Assert.Equal(2, close.VersionNumber);
        Assert.Contains("Record the laboratory outcome for v2", close.Title);
        Assert.Contains(close.Evidence, e => e.Contains("predicted success") && e.Contains("2026-01-02"));
        Assert.Equal(1, plan.OutcomesRecorded);
    }

    [Fact(DisplayName = "The limit trims the plan after ordering leads, repetition and loop closing")]
    public void Limit_TrimsOrderedPlan()
    {
        var versions = new[]
        {
            Version(Guid.NewGuid(), 1, hasOutcome: true, succeeded: true, predicted: 0.9, components: [("Aspirin", 1.0)]),
            Version(Guid.NewGuid(), 2, predicted: 0.6, components: [("Aspirin", 1.0)]),
            Version(Guid.NewGuid(), 3, predicted: 0.5, components: [("Aspirin", 1.0)]),
        };
        var leads = new[]
        {
            Lead(versions[1].Id, 0.8, 0.75, ("temperatureCelsius", 30)),
            Lead(versions[2].Id, 0.7, 0.65, ("temperatureCelsius", 28)),
        };

        var plan = NextExperimentPlanner.Build(FormulationId, versions, leads, limit: 2);

        Assert.Equal(2, plan.Recommendations.Count);
        Assert.Equal(["SimulationLead", "SimulationLead"], plan.Recommendations.Select(r => r.Kind).ToList());

        var full = NextExperimentPlanner.Build(FormulationId, versions, leads, limit: 5);
        Assert.Equal(
            ["SimulationLead", "SimulationLead", "RepeatSuccess", "CloseLoop"],
            full.Recommendations.Take(4).Select(r => r.Kind).ToList());
    }

    /// <summary>Строит план с одним непроверенным лидом и заданными парами «прогноз → исход».</summary>
    private static NextExperimentPlanDto BuildWithOutcomes(params (double Predicted, bool Succeeded)[] outcomes)
    {
        var versions = new List<PlannerVersion>();
        for (var i = 0; i < outcomes.Length; i++)
        {
            versions.Add(Version(Guid.NewGuid(), i + 1, hasOutcome: true, succeeded: outcomes[i].Succeeded,
                predicted: outcomes[i].Predicted, components: [("Aspirin", 1.0)]));
        }

        var pending = Guid.NewGuid();
        versions.Add(Version(pending, outcomes.Length + 1, predicted: 0.5, components: [("Aspirin", 1.0)]));

        return NextExperimentPlanner.Build(FormulationId, versions,
            [Lead(pending, 0.8, 0.75, ("temperatureCelsius", 30))], limit: 10);
    }
}
