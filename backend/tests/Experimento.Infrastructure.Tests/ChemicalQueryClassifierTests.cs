using Experimento.Infrastructure.Chemicals;

namespace Experimento.Infrastructure.Tests;

/// <summary>
/// Тесты распознавания типа запроса в поиске по каталогу: имя, CAS, формула.
/// </summary>
public class ChemicalQueryClassifierTests
{
    [Theory]
    [InlineData("aspirin")]
    [InlineData("sodium chloride")]
    [InlineData("Fe")]        // одиночный элемент без индекса — трактуем как имя
    [InlineData("ethanol 96%")]
    public void Classify_AsName(string query)
        => Assert.Equal(ChemicalQueryKind.Name, ChemicalQueryClassifier.Classify(query));

    [Theory]
    [InlineData("50-78-2")]
    [InlineData("7647-14-5")]
    [InlineData("7732-18-5")]
    public void Classify_AsCas(string query)
        => Assert.Equal(ChemicalQueryKind.Cas, ChemicalQueryClassifier.Classify(query));

    [Theory]
    [InlineData("H2O")]
    [InlineData("C9H8O4")]
    [InlineData("NaCl")]      // два элемента без индексов
    [InlineData("CH3Cl")]
    [InlineData("ClNa")]      // формула в порядке PubChem (не Hill)
    [InlineData("O2")]        // один элемент, но с индексом > 1
    [InlineData("C12H22O11")]
    public void Classify_AsFormula(string query)
        => Assert.Equal(ChemicalQueryKind.Formula, ChemicalQueryClassifier.Classify(query));

    [Theory]
    [InlineData("glucose")]   // обычные слова не являются формулами
    [InlineData("C9H8x4")]    // неизвестный «элемент»
    [InlineData("")]
    public void NotAFormula(string query)
        => Assert.NotEqual(ChemicalQueryKind.Formula, ChemicalQueryClassifier.Classify(query));
}
