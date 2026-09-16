namespace Experimento.Domain.Entities;

/// <summary>
/// A single chemical component in a formulation version.
/// </summary>
public class FormulationComponent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VersionId { get; set; }
    public FormulationVersion Version { get; set; } = null!;
    public string ChemicalName { get; set; } = string.Empty;
    public string? CasNumber { get; set; }
    public string? Formula { get; set; }
    public double MolarMass { get; set; }
    public double Proportion { get; set; }
    public string? Role { get; set; }

    /// <summary>
    /// PubChem CID выбранного вещества. Обязателен: компонент можно создать
    /// только из верифицированного каталога; свойства копируются из каталога.
    /// </summary>
    public int? PubChemCid { get; set; }

    /// <summary>Канонический SMILES из каталога (снимок структуры на момент создания версии).</summary>
    public string? Smiles { get; set; }
}
