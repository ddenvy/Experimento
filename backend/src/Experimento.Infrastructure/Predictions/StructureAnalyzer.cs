using System.Text.RegularExpressions;

namespace Experimento.Infrastructure.Predictions;

/// <summary>
/// Результат структурного скрининга одного компонента.
/// </summary>
public sealed record StructuralAnalysis(
    bool HeavyMetal,
    bool EnergeticGroup,
    bool ReactiveGroup,
    bool Halogenated,
    IReadOnlyList<string> Findings)
{
    public bool HasHazards => HeavyMetal || EnergeticGroup || ReactiveGroup || Halogenated;

    public static StructuralAnalysis Empty { get; } =
        new(false, false, false, false, Array.Empty<string>());
}

/// <summary>
/// Детерминированный скрининг структуры по данным каталога PubChem:
/// молекулярная формула (элементный состав в Hill-нотации) и канонический SMILES
/// (функциональные группы). Заменяет наивный поиск ключевых слов в названии.
/// Это скрининговый инструмент для генерации гипотез, а не токсикологическая экспертиза.
/// </summary>
public static class StructureAnalyzer
{
    // Токсичные тяжёлые металлы/металлоиды (консервативный список).
    private static readonly HashSet<string> HeavyMetalElements = new(new[]
        { "As", "Hg", "Pb", "Cd", "Tl", "Sb", "Be" }, StringComparer.Ordinal);

    private static readonly HashSet<string> HalogenElements = new(new[]
        { "F", "Cl", "Br", "I" }, StringComparer.Ordinal);

    // Элемент формулы: заглавная + опциональная строчная + опциональный счётчик (C, Cl, O4).
    private static readonly Regex ElementToken = new(@"([A-Z][a-z]?)(\d*)", RegexOptions.Compiled);

    // В SMILES ионы записаны в квадратных скобках: [Na+], [Cl-]. Их нужно исключить,
    // чтобы поваренная соль не считалась «галогенированной» — нас интересует ковалентно
    // связанный галоген (C-Cl и т.п.), который записывается вне скобок.
    private static readonly Regex BracketedAtom = new(@"\[[^\]]*\]", RegexOptions.Compiled);
    private static readonly Regex CovalentHalogen =
        new(@"(?<![A-Za-z\]])(?:Cl|Br|F|I)(?![A-Za-z])", RegexOptions.Compiled);

    // Запасной сигнал: термины в названии (актуально для смесей без SMILES).
    private static readonly (string Term, string Category, string Finding)[] NameTerms =
    {
        ("arsenic", "heavy", "term 'arsenic' in component name"),
        ("mercury", "heavy", "term 'mercury' in component name"),
        ("lead", "heavy", "term 'lead' in component name"),
        ("cadmium", "heavy", "term 'cadmium' in component name"),
        ("nitro", "energetic", "term 'nitro' in component name"),
        ("azide", "energetic", "term 'azide' in component name"),
        ("cyanide", "reactive", "term 'cyanide' in component name"),
        ("phosgene", "reactive", "term 'phosgene' in component name"),
        ("hydrazine", "reactive", "term 'hydrazine' in component name"),
    };

    public static StructuralAnalysis Analyze(string? formula, string? smiles, string? name)
    {
        var elements = ParseFormula(formula);
        var findings = new List<string>();

        // --- Элементный состав (по формуле) ---
        var heavyElements = elements.Keys.Where(HeavyMetalElements.Contains).OrderBy(e => e).ToList();
        var heavy = heavyElements.Count > 0;
        if (heavy)
            findings.Add($"heavy-metal element ({string.Join(", ", heavyElements)})");

        // --- Функциональные группы (по SMILES) ---
        var s = smiles ?? string.Empty;
        var hasSmiles = !string.IsNullOrWhiteSpace(s);

        var nitro = hasSmiles &&
                    (s.Contains("[N+](=O)[O-]", StringComparison.Ordinal) ||
                     s.Contains("[N+]([O-])=O", StringComparison.Ordinal));
        var azide = hasSmiles &&
                    (s.Contains("=[N+]=[N-]", StringComparison.Ordinal) ||
                     s.Contains("[N-]=[N+]", StringComparison.Ordinal));
        var energetic = nitro || azide;
        if (nitro) findings.Add("nitro group (SMILES)");
        if (azide) findings.Add("azide group (SMILES)");

        var nitrile = hasSmiles && s.Contains("C#N", StringComparison.Ordinal);
        var isocyanate = hasSmiles && s.Contains("N=C=O", StringComparison.Ordinal);
        var peroxide = hasSmiles && s.Contains("OO", StringComparison.Ordinal);
        var reactive = nitrile || isocyanate || peroxide;
        if (nitrile) findings.Add("nitrile group (SMILES)");
        if (isocyanate) findings.Add("isocyanate group (SMILES)");
        if (peroxide) findings.Add("peroxide bond (SMILES)");

        // Ковалентный галоген — по SMILES вне ионных скобок; без SMILES — грубо по формуле.
        bool halogenated;
        if (hasSmiles)
        {
            var stripped = BracketedAtom.Replace(s, string.Empty);
            halogenated = CovalentHalogen.IsMatch(stripped);
        }
        else
        {
            halogenated = elements.Keys.Any(HalogenElements.Contains);
        }
        if (halogenated)
            findings.Add(hasSmiles ? "covalently bound halogen (SMILES)" : "halogen in formula");

        // --- Запасной сигнал по названию (может только усилить категорию) ---
        var haystack = (name ?? string.Empty).ToLowerInvariant();
        foreach (var (term, category, finding) in NameTerms)
        {
            if (!haystack.Contains(term, StringComparison.Ordinal)) continue;
            if (category == "heavy") heavy = true;
            if (category == "energetic") energetic = true;
            if (category == "reactive") reactive = true;
            if (!findings.Contains(finding)) findings.Add(finding);
        }

        return new StructuralAnalysis(heavy, energetic, reactive, halogenated, findings);
    }

    /// <summary>
    /// Разбирает формулу в Hill-нотации в карту элемент → количество атомов (C9H8O4, ClNa).
    /// </summary>
    public static IReadOnlyDictionary<string, int> ParseFormula(string? formula)
    {
        var result = new Dictionary<string, int>();
        if (string.IsNullOrWhiteSpace(formula)) return result;

        foreach (Match match in ElementToken.Matches(formula))
        {
            var element = match.Groups[1].Value;
            var count = match.Groups[2].Success && match.Groups[2].Length > 0
                ? int.Parse(match.Groups[2].Value)
                : 1;
            result[element] = result.GetValueOrDefault(element) + count;
        }
        return result;
    }
}
