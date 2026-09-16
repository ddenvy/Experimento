using Experimento.Domain.Enums;

namespace Experimento.Domain.Entities;

/// <summary>
/// Регуляторный статус вещества по конкретному органу/списку.
/// Одна запись = одно вещество + один регулятор + его статус + детали.
/// </summary>
public class ChemicalRegulation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK на каталог веществ.</summary>
    public Guid ChemicalCatalogEntryId { get; set; }
    public ChemicalCatalogEntry ChemicalCatalogEntry { get; set; } = null!;

    /// <summary>Какой регулятор / список.</summary>
    public RegulationAuthority Authority { get; set; }

    /// <summary>Степень ограничения.</summary>
    public RegulationStatus Status { get; set; }

    /// <summary>Краткое человекочитаемое обоснование (например, "SVHC candidate list - reprotoxic").</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Ссылка на первоисточник (страница регулятора, номер в списке).</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Дата последней актуализации записи.</summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
