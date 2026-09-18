using System.Globalization;
using System.Text.Json.Serialization;

namespace Experimento.Application.Features.Formulations;

/// <summary>Насколько рекомендация опирается на лабораторные данные, а не только на модель.</summary>
public enum NextExperimentConfidence
{
    Low,
    Medium,
    High
}

/// <summary>Предлагаемое значение параметра эксперимента (доля компонента, температура, pH).</summary>
public record SuggestedParameterDto(string Name, double Value);

/// <summary>
/// Рекомендация следующего эксперимента. Kind: "SimulationLead", "RepeatSuccess" или "CloseLoop".
/// </summary>
public record NextExperimentDto(
    string Kind,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] NextExperimentConfidence Confidence,
    string Title,
    string Rationale,
    IReadOnlyList<string> Evidence,
    Guid? VersionId,
    int? VersionNumber,
    double? PredictedSuccessProbability,
    IReadOnlyList<SuggestedParameterDto> SuggestedParameters);

/// <summary>
/// План следующих экспериментов по формуляции: ранжированный список плюс состояние цикла
/// «прогноз → лабораторный исход», без которого доверие к модели неоткуда взять.
/// </summary>
public record NextExperimentPlanDto(
    Guid FormulationId,
    int VersionsTotal,
    int OutcomesRecorded,
    double? MeanCalibrationError,
    IReadOnlyList<NextExperimentDto> Recommendations);

/// <summary>Компонент версии в том виде, в котором он нужен планировщику.</summary>
public sealed record PlannerComponent(string Name, double Proportion);

/// <summary>Условия процесса версии в том виде, в котором они нужны планировщику.</summary>
public sealed record PlannerConditions(double TemperatureCelsius, double? PhTarget, string? Solvent);

/// <summary>Всё, что известно о версии: состав, условия, прогноз и записанный лабораторный исход.</summary>
public sealed record PlannerVersion(
    Guid Id,
    int VersionNumber,
    IReadOnlyList<PlannerComponent> Components,
    PlannerConditions Conditions,
    bool HasOutcome,
    bool OutcomeSucceeded,
    double? PredictedSuccess,
    DateTime? PredictionCreatedAtUtc);

/// <summary>Лучший кандидат последней завершённой симуляции версии.</summary>
public sealed record PlannerLead(
    Guid VersionId,
    double Score,
    double SuccessProbability,
    IReadOnlyList<SuggestedParameterDto> Parameters);

/// <summary>
/// Правила ранжирования следующих экспериментов. Чистая функция над уже собранными
/// данными: лучший непроверенный кандидат симуляции, воспроизведение удачной версии
/// и напоминание записать исход. Модель без лабораторных исходов даёт только гипотезы,
/// поэтому уверенность рекомендации считается по фактической калибровке.
/// </summary>
public static class NextExperimentPlanner
{
    /// <summary>Ниже этой доли калиброванной ошибки модель считается пригодной для планирования.</summary>
    private const double WellCalibratedError = 0.25;

    /// <summary>Выше этой ошибки прогнозы расходятся с лабораторией настолько, что вести по ним нельзя.</summary>
    private const double PoorlyCalibratedError = 0.40;

    /// <summary>Минимум исходов, после которого калибровке вообще можно доверять.</summary>
    private const int MinOutcomesForTrust = 3;

    public static NextExperimentPlanDto Build(
        Guid formulationId,
        IReadOnlyList<PlannerVersion> versions,
        IReadOnlyList<PlannerLead> leads,
        int limit)
    {
        var ordered = versions.OrderBy(v => v.VersionNumber).ToList();

        // Калибровка считается только по версиям, где есть и прогноз, и исход.
        var evaluated = ordered.Where(v => v.HasOutcome && v.PredictedSuccess.HasValue).ToList();
        var meanError = evaluated.Count == 0
            ? (double?)null
            : evaluated.Average(v => Math.Abs(v.PredictedSuccess!.Value - (v.OutcomeSucceeded ? 1.0 : 0.0)));

        var recommendations = new List<NextExperimentDto>();

        // 1. Непроверенные лиды симуляций: чем выше балл, тем раньше эксперимент.
        var leadConfidence = LeadConfidence(evaluated.Count, meanError);
        foreach (var lead in leads.OrderByDescending(l => l.Score))
        {
            var version = ordered.FirstOrDefault(v => v.Id == lead.VersionId);
            // Версия с записанным исходом уже проверена — повторять её кандидатов не нужно.
            if (version is null || version.HasOutcome) continue;

            recommendations.Add(new NextExperimentDto(
                "SimulationLead",
                leadConfidence,
                LeadTitle(version, lead.Parameters),
                LeadRationale(version, lead, evaluated.Count, meanError),
                LeadEvidence(version, lead),
                version.Id,
                version.VersionNumber,
                lead.SuccessProbability,
                lead.Parameters));
        }

        // 2. Воспроизведение версии, которая уже сработала в лаборатории.
        var winner = ordered.LastOrDefault(v => v.HasOutcome && v.OutcomeSucceeded);
        if (winner is not null)
        {
            var previous = ordered.LastOrDefault(v => v.VersionNumber < winner.VersionNumber);
            var changes = previous is null ? new List<string>() : DescribeChanges(previous, winner);
            var evidence = new List<string> { $"v{winner.VersionNumber} succeeded in the laboratory" };
            if (previous is not null && changes.Count > 0)
                evidence.Add($"change vs v{previous.VersionNumber}: {string.Join("; ", changes)}");

            recommendations.Add(new NextExperimentDto(
                "RepeatSuccess",
                NextExperimentConfidence.High,
                $"Reproduce the winning conditions of v{winner.VersionNumber}",
                previous is null || changes.Count == 0
                    ? "This version already succeeded in the laboratory with no recorded change from the previous one; confirm it reproduces before changing anything."
                    : $"v{winner.VersionNumber} succeeded in the laboratory — reproduce it so the effect can be attributed to the change, not to noise.",
                evidence,
                winner.Id,
                winner.VersionNumber,
                winner.PredictedSuccess,
                Array.Empty<SuggestedParameterDto>()));
        }

        // 3. Закрытие цикла: у версии есть прогноз, но нет лабораторного исхода.
        foreach (var pending in ordered
                     .Where(v => !v.HasOutcome && v.PredictedSuccess.HasValue)
                     .OrderByDescending(v => v.VersionNumber))
        {
            recommendations.Add(new NextExperimentDto(
                "CloseLoop",
                NextExperimentConfidence.High,
                $"Record the laboratory outcome for v{pending.VersionNumber}",
                "The cycle stays open: a prediction without a laboratory outcome cannot calibrate the model, and every later recommendation inherits that gap.",
                new[]
                {
                    $"v{pending.VersionNumber}: predicted success {pending.PredictedSuccess:P0}" +
                    (pending.PredictionCreatedAtUtc is { } at ? $" on {at:yyyy-MM-dd}" : ""),
                    "no outcome recorded for this version",
                },
                pending.Id,
                pending.VersionNumber,
                pending.PredictedSuccess,
                Array.Empty<SuggestedParameterDto>()));
        }

        return new NextExperimentPlanDto(
            formulationId,
            ordered.Count,
            evaluated.Count,
            meanError,
            recommendations.Take(limit).ToList());
    }

    /// <summary>
    /// Без лабораторных исходов прогноз — гипотеза; уверенность растёт только вместе
    /// с числом исходов и падает вместе с ошибкой калибровки.
    /// </summary>
    private static NextExperimentConfidence LeadConfidence(int outcomesRecorded, double? meanError)
    {
        if (outcomesRecorded == 0 || meanError is null) return NextExperimentConfidence.Low;
        if (outcomesRecorded >= MinOutcomesForTrust && meanError <= WellCalibratedError)
            return NextExperimentConfidence.High;
        return meanError <= PoorlyCalibratedError
            ? NextExperimentConfidence.Medium
            : NextExperimentConfidence.Low;
    }

    private static string LeadTitle(PlannerVersion version, IReadOnlyList<SuggestedParameterDto> parameters)
        => parameters.Count == 0
            ? $"Re-test v{version.VersionNumber} unchanged"
            : $"Run v{version.VersionNumber} with {string.Join(", ", parameters.Select(DescribeParameter))}";

    private static string LeadRationale(
        PlannerVersion version, PlannerLead lead, int outcomesRecorded, double? meanError)
    {
        var basis = meanError is { } error && outcomesRecorded > 0
            ? $"The model's mean error on your {outcomesRecorded} recorded outcome(s) is {Pct(error)}."
            : "No laboratory outcomes are recorded yet, so this rests on the model alone.";

        return $"Simulation scored this parameter set {Num(lead.Score, "0.###")} with predicted success " +
               $"{Pct(lead.SuccessProbability)}, and v{version.VersionNumber} has no laboratory outcome yet. {basis}";
    }

    private static IReadOnlyList<string> LeadEvidence(PlannerVersion version, PlannerLead lead)
    {
        var evidence = new List<string>
        {
            $"simulation score {Num(lead.Score, "0.###")} · predicted success {Pct(lead.SuccessProbability)}"
        };
        if (version.PredictedSuccess.HasValue)
            evidence.Add($"v{version.VersionNumber} prediction: {Pct(version.PredictedSuccess.Value)} success");
        evidence.Add("no laboratory outcome recorded for this version");
        return evidence;
    }

    private static string DescribeParameter(SuggestedParameterDto parameter)
    {
        const string proportionSuffix = ".proportion";
        if (parameter.Name.EndsWith(proportionSuffix, StringComparison.Ordinal))
            return $"{parameter.Name[..^proportionSuffix.Length]} at {Num(parameter.Value, "0.###")}";

        return parameter.Name switch
        {
            "temperatureCelsius" => $"{Num(parameter.Value, "0.#")} °C",
            "phTarget" => $"pH {Num(parameter.Value, "0.##")}",
            _ => $"{parameter.Name} = {Num(parameter.Value, "0.###")}",
        };
    }

    /// <summary>Отличия состава и условий между двумя версиями — в виде, пригодном для чтения.</summary>
    private static List<string> DescribeChanges(PlannerVersion from, PlannerVersion to)
    {
        var changes = new List<string>();
        var names = from.Components.Select(c => c.Name)
            .Concat(to.Components.Select(c => c.Name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var name in names)
        {
            var before = from.Components.FirstOrDefault(c => c.Name == name);
            var after = to.Components.FirstOrDefault(c => c.Name == name);
            if (before is null && after is not null)
                changes.Add($"{name} added at {Num(after.Proportion, "0.###")}");
            else if (after is null && before is not null) changes.Add($"{name} removed");
            else if (before is not null && after is not null && Math.Abs(before.Proportion - after.Proportion) > 0.005)
                changes.Add($"{name} {Num(before.Proportion, "0.###")} → {Num(after.Proportion, "0.###")}");
        }

        if (Math.Abs(from.Conditions.TemperatureCelsius - to.Conditions.TemperatureCelsius) > 1)
            changes.Add($"temperature {Num(from.Conditions.TemperatureCelsius, "0.#")} → {Num(to.Conditions.TemperatureCelsius, "0.#")} °C");

        if (from.Conditions.PhTarget is { } phFrom && to.Conditions.PhTarget is { } phTo && Math.Abs(phFrom - phTo) > 0.1)
            changes.Add($"pH {Num(phFrom, "0.##")} → {Num(phTo, "0.##")}");

        if (!string.Equals(from.Conditions.Solvent, to.Conditions.Solvent, StringComparison.OrdinalIgnoreCase))
            changes.Add($"solvent {from.Conditions.Solvent ?? "—"} → {to.Conditions.Solvent ?? "—"}");

        return changes;
    }

    /// <summary>
    /// Числа в тексте не должны зависеть от культуры хоста: интерфейс приложения англоязычный,
    /// а тесты и логи обязаны воспроизводиться на любой машине.
    /// </summary>
    private static string Num(double value, string format)
        => value.ToString(format, CultureInfo.InvariantCulture);

    private static string Pct(double value)
        => value.ToString("P0", CultureInfo.InvariantCulture);
}
