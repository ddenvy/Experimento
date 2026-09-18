using Experimento.Domain.Enums;
using Experimento.Infrastructure.Chemicals;
using Experimento.Infrastructure.Predictions;
using static Experimento.Infrastructure.Chemicals.SubstituteScoring;

namespace Experimento.Infrastructure.Tests;

/// <summary>
/// Тесты правил близости веществ при подборе замен: состав, масса, класс опасности
/// и регуляторный штраф к ранжирующему баллу.
/// </summary>
public class SubstituteScoringTests
{
    // Бензойная кислота и 4-гидроксибензальдегид — изомеры (C7H6O2).
    private static ChemicalProfile BenzoicAcid()
        => ChemicalProfile.From("C7H6O2", "O=C(O)c1ccccc1", "Benzoic acid", 122.12);

    private static ChemicalProfile Hydroxybenzaldehyde()
        => ChemicalProfile.From("C7H6O2", "O=Cc1ccc(O)cc1", "4-Hydroxybenzaldehyde", 122.12);

    private static ChemicalProfile Hexane()
        => ChemicalProfile.From("C6H14", "CCCCCC", "Hexane", 86.18);

    [Fact(DisplayName = "Identical substance yields full similarity and isomer signal")]
    public void IdenticalSubstance_ScoresOne()
    {
        var result = Compute(BenzoicAcid(), BenzoicAcid(), RegulationStatus.Compliant);

        Assert.Equal(1.0, result.Similarity, 4);
        Assert.Equal(1.0, result.MatchScore, 4);
        Assert.Contains("same molecular formula (isomer)", result.Signals);
        Assert.Contains(result.Signals, s => s.StartsWith("elemental composition match", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Isomer of the same formula outranks a structurally distant solvent")]
    public void Isomer_OutranksDistantCompound()
    {
        var isomer = Compute(BenzoicAcid(), Hydroxybenzaldehyde(), RegulationStatus.Compliant);
        var distant = Compute(BenzoicAcid(), Hexane(), RegulationStatus.Compliant);

        Assert.True(isomer.Similarity > distant.Similarity);
        Assert.Contains("same molecular formula (isomer)", isomer.Signals);
        Assert.DoesNotContain("same molecular formula (isomer)", distant.Signals);
        Assert.Contains(distant.Signals, s => s.StartsWith("elemental composition match", StringComparison.Ordinal));
        Assert.Contains(distant.Signals, s => s.StartsWith("molar mass Δ", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Regulatory status scales the ranking score without hiding the candidate")]
    public void RegulatoryStatus_ScalesMatchScore()
    {
        var compliant = Compute(BenzoicAcid(), Hexane(), RegulationStatus.Compliant);
        var restricted = Compute(BenzoicAcid(), Hexane(), RegulationStatus.Restricted);
        var banned = Compute(BenzoicAcid(), Hexane(), RegulationStatus.Banned);

        // Similarity отражает только свойства и не зависит от регуляторного статуса.
        Assert.Equal(compliant.Similarity, restricted.Similarity, 4);
        Assert.Equal(compliant.Similarity, banned.Similarity, 4);

        Assert.Equal(compliant.Similarity, compliant.MatchScore, 4);
        Assert.Equal(compliant.Similarity * BannedPenalty, banned.MatchScore, 3);
        Assert.Equal(compliant.Similarity * RestrictedPenalty, restricted.MatchScore, 3);
        Assert.True(banned.MatchScore < restricted.MatchScore);
        Assert.True(restricted.MatchScore < compliant.MatchScore);
    }

    [Fact(DisplayName = "A banned near-identical analogue ranks below a compliant isomer")]
    public void BannedPenalty_CanReorderCandidates()
    {
        var target = BenzoicAcid();
        // Изомер без регуляторных ограничений должен обойти запрещённый аналог.
        var compliantIsomer = Compute(target, Hydroxybenzaldehyde(), RegulationStatus.Compliant);
        var bannedIsomer = Compute(target, Hydroxybenzaldehyde(), RegulationStatus.Banned);

        Assert.True(compliantIsomer.MatchScore > bannedIsomer.MatchScore);
        Assert.Equal(bannedIsomer.Similarity, compliantIsomer.Similarity, 4);
    }

    [Fact(DisplayName = "Missing composition data is neutral, not penalized")]
    public void MissingComposition_IsNeutral()
    {
        var unknown = ChemicalProfile.From(null, null, "Unknown substance", 0);
        var result = Compute(BenzoicAcid(), unknown, RegulationStatus.Compliant);

        // Все три сигнала нейтральны (0.5) — вещество без структуры не должно
        // получать балл за «отсутствие опасных групп».
        Assert.Equal(0.5, result.Similarity, 4);
        Assert.Contains("molar mass unknown for one side", result.Signals);
        Assert.Contains("hazard profile unknown", result.Signals);
        Assert.DoesNotContain("same molecular formula (isomer)", result.Signals);
    }

    [Theory(DisplayName = "Weighted Tanimoto over elemental composition")]
    [InlineData("C6H14", "C6H14", 1.0)]
    [InlineData("C6", "O6", 0.0)]
    [InlineData("C7H6O2", "C6H14", 0.5217)]
    public void Tanimoto_ElementalComposition(string left, string right, double expected)
    {
        var a = StructureAnalyzer.ParseFormula(left);
        var b = StructureAnalyzer.ParseFormula(right);
        Assert.Equal(expected, Tanimoto(a, b), 4);
    }

    [Theory(DisplayName = "Molar mass similarity decays with relative difference")]
    [InlineData(120.0, 120.0, 1.0)]
    [InlineData(100.0, 50.0, 0.5)]
    [InlineData(0.0, 50.0, 0.5)]
    public void MassSimilarity_Cases(double a, double b, double expected)
        => Assert.Equal(expected, MassSimilarity(a, b), 4);

    [Fact(DisplayName = "Hazard class Jaccard handles empty sets as a match")]
    public void Jaccard_HazardClasses()
    {
        var none = new HashSet<string>(StringComparer.Ordinal);
        var single = new HashSet<string>(new[] { "halogenated" }, StringComparer.Ordinal);
        var other = new HashSet<string>(new[] { "reactive" }, StringComparer.Ordinal);
        var pair = new HashSet<string>(new[] { "halogenated", "reactive" }, StringComparer.Ordinal);

        Assert.Equal(1.0, Jaccard(none, none), 4);
        Assert.Equal(0.0, Jaccard(single, other), 4);
        Assert.Equal(0.5, Jaccard(pair, single), 4);
    }
}
