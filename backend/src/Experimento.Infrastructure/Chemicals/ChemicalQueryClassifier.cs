using System.Text.RegularExpressions;

namespace Experimento.Infrastructure.Chemicals;

/// <summary>Тип химического запроса в строке поиска каталога.</summary>
public enum ChemicalQueryKind
{
    /// <summary>Название или синоним вещества.</summary>
    Name,
    /// <summary>CAS-номер в формате 0000-00-0.</summary>
    Cas,
    /// <summary>Молекулярная формула (H2O, C9H8O4, NaCl).</summary>
    Formula
}

/// <summary>
/// Определяет, чем является строка поиска: названием, CAS-номером или молекулярной
/// формулой. Разбор формулы проводится по реальным символам элементов периодической
/// таблицы, чтобы обычные слова не принимались за формулы.
/// </summary>
public static class ChemicalQueryClassifier
{
    private static readonly Regex CasRegex = new(@"^\d{2,7}-\d{2}-\d$", RegexOptions.Compiled);
    private static readonly Regex TokenRegex = new(@"([A-Z][a-z]?)(\d*)", RegexOptions.Compiled);

    // Символы элементов периодической таблицы (актуальный список).
    private static readonly HashSet<string> ElementSymbols = new(new[]
    {
        "H","He","Li","Be","B","C","N","O","F","Ne","Na","Mg","Al","Si","P","S","Cl","Ar",
        "K","Ca","Sc","Ti","V","Cr","Mn","Fe","Co","Ni","Cu","Zn","Ga","Ge","As","Se","Br","Kr",
        "Rb","Sr","Y","Zr","Nb","Mo","Tc","Ru","Rh","Pd","Ag","Cd","In","Sn","Sb","Te","I","Xe",
        "Cs","Ba","La","Ce","Pr","Nd","Pm","Sm","Eu","Gd","Tb","Dy","Ho","Er","Tm","Yb","Lu",
        "Hf","Ta","W","Re","Os","Ir","Pt","Au","Hg","Tl","Pb","Bi","Po","At","Rn",
        "Fr","Ra","Ac","Th","Pa","U","Np","Pu","Am","Cm","Bk","Cf","Es","Fm","Md","No","Lr",
        "Rf","Db","Sg","Bh","Hs","Mt","Ds","Rg","Cn","Nh","Fl","Mc","Lv","Ts","Og"
    }, StringComparer.Ordinal);

    public static ChemicalQueryKind Classify(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return ChemicalQueryKind.Name;
        var q = query.Trim();

        if (CasRegex.IsMatch(q)) return ChemicalQueryKind.Cas;
        if (LooksLikeFormula(q)) return ChemicalQueryKind.Formula;
        return ChemicalQueryKind.Name;
    }

    /// <summary>
    /// Формула — это последовательность «элемент + опциональный счётчик», где все
    /// токены являются реальными элементами и выражение не вырождается в одиночный
    /// элемент без индекса (это было бы просто названием, например "Fe").
    /// </summary>
    public static bool LooksLikeFormula(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;

        var matches = TokenRegex.Matches(query);
        if (matches.Count == 0) return false;

        // Вся строка должна быть покрыта токенами, без посторонних символов.
        var consumed = matches.Sum(m => m.Length);
        if (consumed != query.Trim().Length) return false;

        var distinct = new HashSet<string>();
        var hasIndexAboveOne = false;
        foreach (Match m in matches)
        {
            var element = m.Groups[1].Value;
            if (!ElementSymbols.Contains(element)) return false;
            distinct.Add(element);
            if (m.Groups[2].Length > 0 && int.Parse(m.Groups[2].Value) > 1)
                hasIndexAboveOne = true;
        }

        return distinct.Count >= 2 || hasIndexAboveOne;
    }
}
