using Experimento.Infrastructure.Predictions;

namespace Experimento.Infrastructure.Tests;

/// <summary>
/// Тесты структурного скрининга: элементный состав по формуле и группы по SMILES.
/// </summary>
public class StructureAnalyzerTests
{
    [Fact]
    public void ParseFormula_HillNotation()
    {
        var elements = StructureAnalyzer.ParseFormula("C9H8O4");
        Assert.Equal(9, elements["C"]);
        Assert.Equal(8, elements["H"]);
        Assert.Equal(4, elements["O"]);
    }

    [Fact]
    public void ParseFormula_HandlesMultiCharElements()
    {
        var elements = StructureAnalyzer.ParseFormula("ClNa");
        Assert.Equal(1, elements["Cl"]);
        Assert.Equal(1, elements["Na"]);
    }

    [Fact]
    public void Aspirin_IsClean()
    {
        var a = StructureAnalyzer.Analyze("C9H8O4", "CC(=O)Oc1ccccc1C(=O)O", "Aspirin");
        Assert.False(a.HasHazards);
        Assert.Empty(a.Findings);
    }

    [Fact]
    public void SodiumChloride_IsNotFlaggedAsHalogenated_IonicBond()
    {
        // Ковалентный галоген опаснее ионного: [Cl-] в скобках должен игнорироваться.
        var a = StructureAnalyzer.Analyze("ClNa", "[Na+].[Cl-]", "Sodium chloride");
        Assert.False(a.Halogenated);
        Assert.False(a.HasHazards);
    }

    [Fact]
    public void NitroGroup_DetectedFromSmiles()
    {
        var a = StructureAnalyzer.Analyze("CH3NO2", "C[N+](=O)[O-]", "Nitromethane");
        Assert.True(a.EnergeticGroup);
        Assert.Contains(a.Findings, f => f.Contains("nitro"));
    }

    [Fact]
    public void AzideGroup_DetectedFromSmiles()
    {
        var a = StructureAnalyzer.Analyze("N3Na", "[Na+].[N-]=[N+]=[N-]", "Sodium azide");
        Assert.True(a.EnergeticGroup);
        Assert.Contains(a.Findings, f => f.Contains("azide"));
        // Ионный натрий не должен считаться тяжёлым металлом/галогеном.
        Assert.False(a.HeavyMetal);
    }

    [Fact]
    public void Nitrile_DetectedFromSmiles()
    {
        var a = StructureAnalyzer.Analyze("C2H3N", "CC#N", "Acetonitrile");
        Assert.True(a.ReactiveGroup);
        Assert.Contains(a.Findings, f => f.Contains("nitrile"));
    }

    [Fact]
    public void CovalentHalogen_DetectedFromSmiles()
    {
        // Фосген: хлор вне ионных скобок.
        var a = StructureAnalyzer.Analyze("COCl2", "O=C(Cl)Cl", "Phosgene");
        Assert.True(a.Halogenated);
    }

    [Fact]
    public void Halogen_FromFormulaFallback_WhenSmilesMissing()
    {
        var a = StructureAnalyzer.Analyze("C2H3Cl", null, "Vinyl chloride");
        Assert.True(a.Halogenated);
    }

    [Fact]
    public void HeavyMetal_DetectedFromFormula()
    {
        var a = StructureAnalyzer.Analyze("Pb", null, "Lead");
        Assert.True(a.HeavyMetal);
        Assert.Contains(a.Findings, f => f.Contains("Pb"));
    }

    [Fact]
    public void Peroxide_DetectedFromSmiles()
    {
        var a = StructureAnalyzer.Analyze("H2O2", "OO", "Hydrogen peroxide");
        Assert.True(a.ReactiveGroup);
        Assert.Contains(a.Findings, f => f.Contains("peroxide"));
    }
}
