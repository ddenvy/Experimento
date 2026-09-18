using Experimento.Application.Abstractions;
using Experimento.Domain.Entities;
using Experimento.Infrastructure.ScaleUp;

namespace Experimento.Infrastructure.Tests;

/// <summary>
/// Тесты правил оценки масштабирования: тепловой режим, газовыделение, класс растворителя,
/// экстремальный pH и пробелы в данных. Все входы задаются явно — расчёт не обращается к БД.
/// </summary>
public class ScaleUpAssessorTests
{
    private readonly ScaleUpAssessor _sut = new();

    // Аспирин: опасных структурных фрагментов нет.
    private const string AspirinFormula = "C9H8O4";
    private const string AspirinSmiles = "CC(=O)Oc1ccccc1C(=O)O";

    // Нитробензол: энергетическая группа (сигнал тепловыделения).
    private const string NitrobenzeneFormula = "C6H5NO2";
    private const string NitrobenzeneSmiles = "c1ccc(cc1)[N+](=O)[O-]";

    // Метилизоцианат: реакционная группа (газовыделение при гидролизе).
    private const string MethylIsocyanateFormula = "C2H3NO";
    private const string MethylIsocyanateSmiles = "CN=C=O";

    private static FormulationComponent Component(string name, string? formula, string? smiles,
        double proportion = 1.0)
        => new()
        {
            ChemicalName = name,
            Formula = formula,
            Smiles = smiles,
            MolarMass = 100,
            Proportion = proportion
        };

    private static FormulationConditions Conditions(
        double temperature = 25, double? pressure = 101.3, double? ph = 7, string? solvent = "water")
        => new()
        {
            TemperatureCelsius = temperature,
            PressureKPa = pressure,
            PhTarget = ph,
            Solvent = solvent
        };

    private ScaleUpAssessmentDto Assess(
        IReadOnlyList<FormulationComponent> components, FormulationConditions conditions, double volume = 10)
        => _sut.Assess(Guid.NewGuid(), 3, components, conditions, volume);

    private static ScaleUpFindingDto Finding(ScaleUpAssessmentDto assessment, string factor)
        => assessment.Findings.Single(f => f.Factor == factor);

    [Fact(DisplayName = "Inert formulation at lab scale raises no findings and scores 100")]
    public void InertFormulation_AtLabScale_IsClean()
    {
        var assessment = Assess(
            [Component("Aspirin", AspirinFormula, AspirinSmiles)],
            Conditions(),
            volume: ScaleUpAssessor.LabReferenceVolumeLitres);

        Assert.Empty(assessment.Findings);
        Assert.Equal(100, assessment.ReadinessScore);
        Assert.Equal("Ready for pilot scale", assessment.Verdict);
        Assert.Equal(1.0, assessment.ScaleFactor, 3);
        Assert.Equal(3, assessment.VersionNumber);
    }

    [Fact(DisplayName = "Heat-releasing chemistry escalates from medium at bench scale to critical at pilot scale")]
    public void EnergeticComponent_EscalatesWithScale()
    {
        var components = new[] { Component("Nitrobenzene", NitrobenzeneFormula, NitrobenzeneSmiles) };

        var bench = Finding(Assess(components, Conditions(), volume: 2), "Thermal");
        var pilot = Finding(Assess(components, Conditions(), volume: 50), "Thermal");

        Assert.Equal(ScaleUpSeverity.Medium, bench.Severity);
        Assert.Equal(ScaleUpSeverity.Critical, pilot.Severity);
        Assert.Contains("Nitrobenzene", pilot.Observation);
        // Теплоотвод на литр падает как кубический корень масштаба.
        Assert.Contains("cooling capacity per litre", pilot.Observation);
    }

    [Fact(DisplayName = "A large volume alone degrades cooling even without structural alerts")]
    public void NoHeatDriver_LargeVolume_IsStillFlagged()
    {
        var pilot = Finding(Assess([Component("Aspirin", AspirinFormula, AspirinSmiles)], Conditions(), volume: 10),
            "Thermal");

        Assert.Equal(ScaleUpSeverity.Medium, pilot.Severity);
        Assert.Contains("no exothermic structural alerts", pilot.Observation);

        // На лабораторном масштабе без экзотермических сигналов замечаний нет.
        Assert.DoesNotContain(
            Assess([Component("Aspirin", AspirinFormula, AspirinSmiles)], Conditions(), volume: 1).Findings,
            f => f.Factor == "Thermal");
    }

    [Fact(DisplayName = "Gas-forming chemistry in a sealed vessel is high severity")]
    public void ReactiveComponent_SealedVessel_HighGasSeverity()
    {
        var assessment = Assess(
            [Component("Methyl isocyanate", MethylIsocyanateFormula, MethylIsocyanateSmiles)],
            Conditions(pressure: 300));

        var gas = Finding(assessment, "Gas evolution");
        Assert.Equal(ScaleUpSeverity.High, gas.Severity);
        Assert.Contains("sealed vessel", gas.Observation);
        Assert.Contains("Methyl isocyanate", gas.Observation);
        Assert.Equal(ScaleUpSeverity.High, Finding(assessment, "Thermal").Severity);

        var vented = Finding(Assess(
            [Component("Methyl isocyanate", MethylIsocyanateFormula, MethylIsocyanateSmiles)],
            Conditions(pressure: 101.3)), "Gas evolution");
        Assert.Equal(ScaleUpSeverity.Medium, vented.Severity);
        Assert.Contains("vented vessel", vented.Observation);
    }

    [Fact(DisplayName = "Solvent whose flash point is below the process temperature is flagged")]
    public void FlammableSolvent_BelowProcessTemperature_IsFlagged()
    {
        var assessment = Assess(
            [Component("Aspirin", AspirinFormula, AspirinSmiles)],
            Conditions(temperature: 25, solvent: "diethyl ether"));

        var solvent = Finding(assessment, "Solvent");
        Assert.Equal(ScaleUpSeverity.High, solvent.Severity);
        Assert.Contains("diethyl ether", solvent.Observation);
    }

    [Fact(DisplayName = "Solvent that stays below its flash point is not flagged")]
    public void NonFlammableSolvent_IsNotFlagged()
    {
        var assessment = Assess(
            [Component("Aspirin", AspirinFormula, AspirinSmiles)],
            Conditions(temperature: 25, solvent: "water"));

        Assert.DoesNotContain(assessment.Findings, f => f.Factor == "Solvent");
    }

    [Fact(DisplayName = "Methanol is matched exactly, not through the 'ethanol' substring")]
    public void Methanol_IsNotConfusedWithEthanol()
    {
        var components = new[] { Component("Aspirin", AspirinFormula, AspirinSmiles) };

        // 12 °C: метанол (11 °C) уже горит, этанол (13 °C) — ещё нет.
        var methanol = Assess(components, Conditions(temperature: 12, solvent: "methanol"));
        var ethanol = Assess(components, Conditions(temperature: 12, solvent: "ethanol"));

        var finding = Finding(methanol, "Solvent");
        Assert.Contains("methanol", finding.Observation);
        Assert.Contains("11", finding.Observation);
        Assert.DoesNotContain(ethanol.Findings, f => f.Factor == "Solvent");
    }

    [Fact(DisplayName = "Extreme pH is flagged and neutral pH is not")]
    public void ExtremePh_IsFlagged()
    {
        var components = new[] { Component("Aspirin", AspirinFormula, AspirinSmiles) };

        var acidic = Finding(Assess(components, Conditions(ph: 1.5)), "pH");
        Assert.Equal(ScaleUpSeverity.High, acidic.Severity);
        Assert.Contains("strongly acidic", acidic.Observation);

        var alkaline = Finding(Assess(components, Conditions(ph: 13)), "pH");
        Assert.Contains("strongly alkaline", alkaline.Observation);

        Assert.DoesNotContain(Assess(components, Conditions(ph: 7)).Findings, f => f.Factor == "pH");
    }

    [Fact(DisplayName = "Unclassified components are reported as a data gap, not treated as safe")]
    public void MissingStructuralData_IsReportedAsGap()
    {
        var assessment = Assess(
            [Component("Proprietary blend", null, null)],
            Conditions(pressure: null, ph: null, solvent: null));

        var gap = Finding(assessment, "Data gaps");
        // Все компоненты без структурных данных — это не «чисто», а незнание.
        Assert.Equal(ScaleUpSeverity.Medium, gap.Severity);
        Assert.Contains("Proprietary blend", gap.Observation);
        Assert.Contains("solvent not specified", gap.Observation);
        Assert.Contains("pH not specified", gap.Observation);
        Assert.Contains("vessel pressure not specified", gap.Observation);
        Assert.Contains("Complete the missing process data", gap.Recommendation);
    }

    [Fact(DisplayName = "A solvent missing from the reference table is reported as a gap")]
    public void UnknownSolvent_IsReportedAsGap()
    {
        var assessment = Assess(
            [Component("Aspirin", AspirinFormula, AspirinSmiles)],
            Conditions(solvent: "unobtainium fluid"));

        var gap = Finding(assessment, "Data gaps");
        Assert.Contains("flash point of 'unobtainium fluid' is not in the reference table", gap.Observation);
        Assert.DoesNotContain(assessment.Findings, f => f.Factor == "Solvent");
    }

    [Fact(DisplayName = "Score and verdict aggregate every finding")]
    public void Score_AggregatesFindings()
    {
        // Critical 35 + High 20 (solvent) + High 20 (pH) + Medium 8 (gas) = 83 → 17 очков.
        var assessment = Assess(
            [Component("Nitrobenzene", NitrobenzeneFormula, NitrobenzeneSmiles)],
            Conditions(temperature: 25, ph: 1.5, solvent: "diethyl ether"),
            volume: 50);

        Assert.Equal(17, assessment.ReadinessScore);
        Assert.Equal("Not ready for scale-up", assessment.Verdict);
        Assert.Equal(50.0, assessment.ScaleFactor, 3);
    }
}
