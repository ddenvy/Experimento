namespace Experimento.Application.Abstractions;

/// <summary>
/// Верифицированное вещество из внешнего каталога (PubChem).
/// </summary>
public record ChemicalDto(
    int PubChemCid,
    string Name,
    string? CasNumber,
    string? Formula,
    double MolarMass);

/// <summary>
/// Доступ к каталогу химических веществ: автоподсказки по названию,
/// резолв конкретного вещества с кэшированием и выборка по набору CID.
/// </summary>
public interface IChemicalCatalogService
{
    /// <summary>
    /// Быстрые подсказки названий для автодополнения (без свойств).
    /// Результаты кэшируются в памяти.
    /// </summary>
    Task<IReadOnlyList<string>> SuggestNamesAsync(string query, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Резолвит вещество по названию через PubChem и кэширует результат в БД.
    /// Возвращает null, если вещество не найдено.
    /// </summary>
    Task<ChemicalDto?> ResolveByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает кэшированные записи каталога по набору CID (один запрос к БД).
    /// Используется для серверной верификации компонентов при создании версии.
    /// </summary>
    Task<IReadOnlyList<ChemicalDto>> GetByCidsAsync(IReadOnlyList<int> cids, CancellationToken cancellationToken = default);
}
