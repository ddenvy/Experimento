using Experimento.Domain.Enums;
using Experimento.Infrastructure.Predictions;

namespace Experimento.Infrastructure.Chemicals;

/// <summary>
/// Расчёт близости двух веществ по эталонным данным каталога.
/// Три независимых сигнала: элементный состав (Tanimoto по брутто-формуле),
/// молярная масса и класс опасности (структурный скрининг). Совпадение формулы
/// (изомеры) даёт надбавку — это ближайшие по поведению аналоги.
/// Чистая функция без обращений к БД, чтобы правила можно было проверять точно.
/// </summary>
public static class SubstituteScoring
{
    public const double ElementWeight = 0.45;
    public const double MassWeight = 0.30;
    public const double HazardWeight = 0.25;

    /// <summary>Надбавка за совпадение брутто-формулы.</summary>
    public const double IsomerBonus = 0.25;

    // Регуляторный штраф к ранжирующему баллу: запрещённое вещество не должно возглавлять
    // список замен, даже если структурно оно ближе всех. Кандидат при этом не скрывается —
    // статус возвращается клиенту отдельным полем.
    public const double BannedPenalty = 0.75;
    public const double RestrictedPenalty = 0.90;

    public sealed record Result(double Similarity, double MatchScore, IReadOnlyList<string> Signals);

    public static Result Compute(
        ChemicalProfile target, ChemicalProfile candidate, RegulationStatus candidateStatus)
    {
        var isIsomer = target.NormalizedFormula is not null
                       && target.NormalizedFormula == candidate.NormalizedFormula;

        var elementScore = Tanimoto(target.Elements, candidate.Elements);
        var massScore = MassSimilarity(target.Mass, candidate.Mass);
        // Отсутствие опасных групп у вещества без структурных данных — это незнание,
        // а не подтверждённая «чистота», поэтому сигнал нейтральный, а не максимальный.
        var hazardsKnown = target.HasStructuralData && candidate.HasStructuralData;
        var hazardScore = hazardsKnown ? Jaccard(target.Hazards, candidate.Hazards) : 0.5;

        var similarity = ElementWeight * elementScore + MassWeight * massScore + HazardWeight * hazardScore;
        if (isIsomer) similarity = Math.Min(1.0, similarity + IsomerBonus);

        var penalty = candidateStatus switch
        {
            RegulationStatus.Banned => BannedPenalty,
            RegulationStatus.Restricted => RestrictedPenalty,
            _ => 1.0,
        };

        var signals = BuildSignals(isIsomer, elementScore, target.Mass, candidate.Mass,
            target.Hazards, candidate.Hazards, hazardsKnown);

        return new Result(Math.Round(similarity, 4), Math.Round(similarity * penalty, 4), signals);
    }

    /// <summary>Готовый к сравнению профиль вещества: элементы, формула, масса и классы опасности.</summary>
    public sealed record ChemicalProfile(
        IReadOnlyDictionary<string, int> Elements,
        string? NormalizedFormula,
        double Mass,
        IReadOnlySet<string> Hazards,
        bool HasStructuralData)
    {
        public static ChemicalProfile From(string? formula, string? smiles, string name, double mass)
        {
            var analysis = StructureAnalyzer.Analyze(formula, smiles, name);
            return new ChemicalProfile(
                StructureAnalyzer.ParseFormula(formula),
                NormalizeFormula(formula),
                mass,
                HazardClasses(analysis),
                !string.IsNullOrWhiteSpace(formula) || !string.IsNullOrWhiteSpace(smiles));
        }
    }

    /// <summary>Взвешенный Tanimoto по составу: учитывает количество атомов, а не только их наличие.</summary>
    public static double Tanimoto(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b)
    {
        // Нет данных о составе хотя бы с одной стороны — сигнал нейтральный, не штрафуем.
        if (a.Count == 0 || b.Count == 0) return 0.5;

        double intersection = 0, union = 0;
        foreach (var key in a.Keys.Union(b.Keys, StringComparer.Ordinal))
        {
            var left = a.GetValueOrDefault(key);
            var right = b.GetValueOrDefault(key);
            intersection += Math.Min(left, right);
            union += Math.Max(left, right);
        }
        return union == 0 ? 0 : intersection / union;
    }

    public static double MassSimilarity(double a, double b)
    {
        if (a <= 0 || b <= 0) return 0.5;
        return Math.Max(0, 1 - Math.Abs(a - b) / Math.Max(a, b));
    }

    public static double Jaccard(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        // Оба вещества без выявленных опасных групп — профили совпадают.
        if (a.Count == 0 && b.Count == 0) return 1.0;
        var intersection = a.Intersect(b).Count();
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 1.0 : (double)intersection / union;
    }

    private static HashSet<string> HazardClasses(StructuralAnalysis analysis)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (analysis.HeavyMetal) set.Add("heavy-metal");
        if (analysis.EnergeticGroup) set.Add("energetic");
        if (analysis.ReactiveGroup) set.Add("reactive");
        if (analysis.Halogenated) set.Add("halogenated");
        return set;
    }

    private static List<string> BuildSignals(bool isIsomer, double elementScore,
        double targetMass, double candidateMass, IReadOnlySet<string> targetHazards, IReadOnlySet<string> hazards,
        bool hazardsKnown)
    {
        var signals = new List<string>();
        if (isIsomer) signals.Add("same molecular formula (isomer)");
        signals.Add($"elemental composition match {elementScore:P0}");

        if (targetMass > 0 && candidateMass > 0)
            signals.Add($"molar mass Δ {Math.Abs(targetMass - candidateMass):F1} g/mol");
        else
            signals.Add("molar mass unknown for one side");

        if (!hazardsKnown)
        {
            signals.Add("hazard profile unknown");
            return signals;
        }

        var shared = targetHazards.Intersect(hazards).OrderBy(h => h).ToList();
        if (shared.Count > 0) signals.Add($"same hazard class: {string.Join(", ", shared)}");
        else if (targetHazards.Count == 0 && hazards.Count == 0) signals.Add("no hazard class on either side");
        else signals.Add("hazard profile differs");

        return signals;
    }

    private static string? NormalizeFormula(string? formula)
        => string.IsNullOrWhiteSpace(formula) ? null : formula.Trim().ToUpperInvariant();
}
