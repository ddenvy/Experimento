namespace Experimento.Domain.Entities;

/// <summary>
/// Локальный кэш веществ, верифицированных через PubChem.
/// Единственный источник истины для химических свойств компонентов формуляций:
/// клиент не может прислать собственные molar mass / формулу — они берутся отсюда по CID.
/// </summary>
public class ChemicalCatalogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>PubChem Compound ID — стабильный внешний идентификатор вещества.</summary>
    public int PubChemCid { get; set; }

    /// <summary>Каноническое название из PubChem (поле Title).</summary>
    public string CanonicalName { get; set; } = string.Empty;

    /// <summary>Основной CAS-номер, извлечённый из списка синонимов (может отсутствовать).</summary>
    public string? CasNumber { get; set; }

    /// <summary>Молекулярная формула в нотации PubChem, например C9H8O4.</summary>
    public string? Formula { get; set; }

    /// <summary>Молекулярная масса, г/mol (PubChem MolecularWeight).</summary>
    public double MolarMass { get; set; }

    /// <summary>Момент первичного кэширования записи.</summary>
    public DateTime CachedAtUtc { get; set; } = DateTime.UtcNow;
}
