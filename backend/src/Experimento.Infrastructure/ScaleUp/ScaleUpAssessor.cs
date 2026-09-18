using Experimento.Infrastructure.Predictions;

namespace Experimento.Infrastructure.ScaleUp;

/// <summary>
/// Правила оценки масштабирования версии формуляции.
/// Ключевой физический эффект: при росте объёма тепловыделение растёт как L³, а
/// теплоотводящая поверхность — как L², поэтому удельная мощность охлаждения падает
/// пропорционально кубическому корню масштаба. Скрининговый расчёт по правилам —
/// перенос подтверждается термической калориметрией и пилотной партией.
/// </summary>
public class ScaleUpAssessor : IScaleUpAssessment
{
    /// <summary>Лабораторный объём, от которого считается масштаб (типовая колба, 1 л).</summary>
    public const double LabReferenceVolumeLitres = 1.0;

    // Вес фактора в итоговом балле готовности.
    private const int CriticalPenalty = 35;
    private const int HighPenalty = 20;
    private const int MediumPenalty = 8;
    private const int LowPenalty = 3;

    // Пороги масштаба: до 3× лабораторные допущения ещё работают, от 10× начинается пилотный режим.
    private const double PilotScaleFactor = 10;
    private const double BenchScaleFactor = 3;

    /// <summary>Давление, выше которого сосуд считается герметичным (кПа).</summary>
    private const double SealedVesselKPa = 120;

    /// <summary>Температура, с которой заметно ускоряются побочные реакции и испарение (°C).</summary>
    private const double ElevatedTemperatureCelsius = 40;

    /// <summary>
    /// Температуры вспышки типовых растворителей (°C), null — негорючий.
    /// Порядок важен: «ethanol» является подстрокой «methanol», поэтому метанол идёт первым,
    /// а совпадения разрешаются по самой длинной подстроке.
    /// </summary>
    private static readonly (string Name, double? FlashPointCelsius)[] SolventFlashPoints =
    {
        ("methanol", 11.0),
        ("ethanol", 13.0),
        ("isopropanol", 12.0),
        ("2-propanol", 12.0),
        ("diethyl ether", -45.0),
        ("ethyl acetate", -4.0),
        ("acetonitrile", 2.0),
        ("dichloromethane", null),
        ("chloroform", null),
        ("acetone", -20.0),
        ("toluene", 4.0),
        ("hexane", -22.0),
        ("heptane", -4.0),
        ("pentane", -49.0),
        ("dmso", 89.0),
        ("dimethyl sulfoxide", 89.0),
        ("dmf", 58.0),
        ("dimethylformamide", 58.0),
        ("water", null),
    };

    public ScaleUpAssessmentDto Assess(
        Guid versionId,
        int versionNumber,
        IReadOnlyList<FormulationComponent> components,
        FormulationConditions conditions,
        double targetVolumeLitres)
    {
        var scaleFactor = Math.Max(1.0, targetVolumeLitres / LabReferenceVolumeLitres);
        var hazards = components.Select(ToHazard).ToList();

        var findings = new List<ScaleUpFindingDto>();
        findings.AddRange(ThermalFindings(hazards, conditions, scaleFactor));
        findings.AddRange(GasEvolutionFindings(hazards, conditions, scaleFactor));
        findings.AddRange(SolventFindings(conditions, scaleFactor));
        findings.AddRange(PhFindings(conditions, scaleFactor));
        findings.AddRange(DataGapFindings(hazards, conditions));

        var score = Math.Clamp(100 - findings.Sum(Penalty), 0, 100);

        return new ScaleUpAssessmentDto(
            versionId, versionNumber, LabReferenceVolumeLitres, targetVolumeLitres,
            Math.Round(scaleFactor, 3), score, Verdict(score), findings);
    }

    /// <summary>Тепловой режим: удельный теплоотвод падает с ростом объёма, экзотермика усиливает эффект.</summary>
    private static IEnumerable<ScaleUpFindingDto> ThermalFindings(
        IReadOnlyList<ComponentHazard> hazards, FormulationConditions conditions, double scaleFactor)
    {
        var energetic = Names(hazards, h => h.Analysis.EnergeticGroup);
        var reactive = Names(hazards, h => h.Analysis.ReactiveGroup);
        var hasHeatDriver = energetic.Count > 0 || reactive.Count > 0;

        var severity = ScaleUpSeverity.Low;
        if (energetic.Count > 0 && scaleFactor >= PilotScaleFactor) severity = ScaleUpSeverity.Critical;
        else if (hasHeatDriver && scaleFactor >= BenchScaleFactor) severity = ScaleUpSeverity.High;
        else if (hasHeatDriver) severity = ScaleUpSeverity.Medium;
        else if (scaleFactor >= PilotScaleFactor) severity = ScaleUpSeverity.Medium;

        // На лабораторном масштабе без экзотермических сигналов переносить нечего — замечания нет.
        if (severity == ScaleUpSeverity.Low) yield break;

        var coolingPerLitre = 1.0 / Math.Cbrt(scaleFactor);
        var drivers = new List<string>();
        if (energetic.Count > 0) drivers.Add($"heat-releasing structural alerts ({string.Join(", ", energetic)})");
        if (reactive.Count > 0) drivers.Add($"reactive groups ({string.Join(", ", reactive)})");
        if (conditions.TemperatureCelsius >= ElevatedTemperatureCelsius)
            drivers.Add($"process temperature {conditions.TemperatureCelsius:F0} °C");
        var driverText = drivers.Count > 0
            ? string.Join("; ", drivers)
            : "no exothermic structural alerts (heat removal still degrades with volume)";

        var recommendation = severity switch
        {
            ScaleUpSeverity.Critical =>
                "Dose the heat-releasing component at a controlled rate, provide jacket cooling and confirm the heat balance by reaction calorimetry before the pilot batch.",
            ScaleUpSeverity.High =>
                "Add jacket cooling and dose the exothermic component at a controlled rate; confirm the heat balance before the pilot batch.",
            _ => "Increase cooling capacity and confirm the heat balance at the target volume.",
        };

        yield return new ScaleUpFindingDto("Thermal",
            severity,
            $"At {scaleFactor:F1}× scale, cooling capacity per litre is {coolingPerLitre:P0} of bench level; {driverText}.",
            recommendation);
    }

    /// <summary>Газовыделение: доля площади сброса на литр падает с объёмом, в герметичном сосуде это критично.</summary>
    private static IEnumerable<ScaleUpFindingDto> GasEvolutionFindings(
        IReadOnlyList<ComponentHazard> hazards, FormulationConditions conditions, double scaleFactor)
    {
        var gasFormers = Names(hazards, h => h.Analysis.ReactiveGroup || h.Analysis.EnergeticGroup);
        if (gasFormers.Count == 0) yield break;

        var sealedVessel = conditions.PressureKPa is { } kpa && kpa > SealedVesselKPa;
        var severity = sealedVessel
            ? ScaleUpSeverity.High
            : scaleFactor >= PilotScaleFactor
                ? ScaleUpSeverity.Medium
                : ScaleUpSeverity.Low;

        var vessel = sealedVessel
            ? $"sealed vessel at {conditions.PressureKPa:F0} kPa"
            : "vented vessel";

        yield return new ScaleUpFindingDto("Gas evolution",
            severity,
            $"Gas-forming chemistry detected ({string.Join(", ", gasFormers)}) in a {vessel}; relief area per litre falls as volume grows.",
            sealedVessel
                ? "Size pressure relief for the target volume and confirm the gas evolution rate."
                : "Keep the vessel vented and confirm the gas evolution rate at the target volume.");
    }

    /// <summary>Класс растворителя: температура вспышки ниже температуры процесса даёт горючую паровую фазу.</summary>
    private static IEnumerable<ScaleUpFindingDto> SolventFindings(
        FormulationConditions conditions, double scaleFactor)
    {
        var solvent = conditions.Solvent?.Trim();
        if (string.IsNullOrEmpty(solvent)) yield break;

        var entry = FindSolvent(solvent);
        if (entry is null || entry.Value.FlashPointCelsius is null) yield break;
        var flashPoint = entry.Value.FlashPointCelsius.Value;
        if (flashPoint >= conditions.TemperatureCelsius) yield break;

        var severity = scaleFactor >= PilotScaleFactor
            ? ScaleUpSeverity.High
            : scaleFactor >= BenchScaleFactor
                ? ScaleUpSeverity.Medium
                : ScaleUpSeverity.Low;

        yield return new ScaleUpFindingDto("Solvent",
            severity,
            $"{entry.Value.Name} (flash point {flashPoint:F0} °C) is below the process temperature of {conditions.TemperatureCelsius:F0} °C, so the headspace is flammable — and it grows with volume.",
            "Blanket the vessel with inert gas and confirm the vessel and area classification for this solvent class.");
    }

    /// <summary>Экстремальный pH: коррозия материалов сосуда и экзотермическая нейтрализация.</summary>
    private static IEnumerable<ScaleUpFindingDto> PhFindings(
        FormulationConditions conditions, double scaleFactor)
    {
        if (conditions.PhTarget is not { } ph || (ph > 2 && ph < 12)) yield break;

        var severity = scaleFactor >= PilotScaleFactor
            ? ScaleUpSeverity.High
            : scaleFactor >= BenchScaleFactor
                ? ScaleUpSeverity.Medium
                : ScaleUpSeverity.Low;

        var kind = ph <= 2 ? "strongly acidic" : "strongly alkaline";

        yield return new ScaleUpFindingDto("pH",
            severity,
            $"Process is {kind} (pH {ph:F1}): the mass is corrosive to common vessel materials and neutralisation releases heat.",
            "Confirm vessel material compatibility and dose the neutralising agent at a controlled rate.");
    }

    /// <summary>
    /// Пробелы в исходных данных. Неизвестное не считается безопасным: там, где экран
    /// опасностей не выполнен, это фиксируется отдельным фактором.
    /// </summary>
    private static IEnumerable<ScaleUpFindingDto> DataGapFindings(
        IReadOnlyList<ComponentHazard> hazards, FormulationConditions conditions)
    {
        var unclassified = hazards.Where(h => !h.HasStructuralData).Select(h => h.Name).ToList();
        var gaps = new List<string>();

        if (unclassified.Count > 0)
            gaps.Add($"no structure data for {string.Join(", ", unclassified)}");

        var solvent = conditions.Solvent?.Trim();
        if (string.IsNullOrEmpty(solvent))
            gaps.Add("solvent not specified");
        else if (FindSolvent(solvent) is null)
            gaps.Add($"flash point of '{solvent}' is not in the reference table");

        if (conditions.PhTarget is null) gaps.Add("pH not specified");
        if (conditions.PressureKPa is null) gaps.Add("vessel pressure not specified (assumed open)");

        if (gaps.Count == 0) yield break;

        var severity = unclassified.Count > 0 && unclassified.Count == hazards.Count
            ? ScaleUpSeverity.Medium
            : ScaleUpSeverity.Low;

        yield return new ScaleUpFindingDto("Data gaps",
            severity,
            $"Scale-up screen is incomplete: {string.Join("; ", gaps)}.",
            "Complete the missing process data so the hazard screen covers the whole formulation.");
    }

    private static ComponentHazard ToHazard(FormulationComponent component) => new(
        component.ChemicalName,
        !string.IsNullOrWhiteSpace(component.Formula) || !string.IsNullOrWhiteSpace(component.Smiles),
        StructureAnalyzer.Analyze(component.Formula, component.Smiles, component.ChemicalName));

    /// <summary>Самое длинное совпадение с таблицей, чтобы «methanol» не распознавался как «ethanol».</summary>
    private static (string Name, double? FlashPointCelsius)? FindSolvent(string solvent)
    {
        var matches = SolventFlashPoints
            .Where(s => solvent.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Name.Length)
            .ToList();
        return matches.Count > 0 ? matches[0] : null;
    }

    private static List<string> Names(
        IReadOnlyList<ComponentHazard> hazards, Func<ComponentHazard, bool> predicate)
        => hazards.Where(predicate).Select(h => h.Name).ToList();

    private static int Penalty(ScaleUpFindingDto finding) => finding.Severity switch
    {
        ScaleUpSeverity.Critical => CriticalPenalty,
        ScaleUpSeverity.High => HighPenalty,
        ScaleUpSeverity.Medium => MediumPenalty,
        _ => LowPenalty,
    };

    private static string Verdict(int score) => score switch
    {
        >= 95 => "Ready for pilot scale",
        >= 75 => "Scalable with controls",
        >= 50 => "Bench validation required",
        _ => "Not ready for scale-up",
    };

    private sealed record ComponentHazard(string Name, bool HasStructuralData, StructuralAnalysis Analysis);
}
