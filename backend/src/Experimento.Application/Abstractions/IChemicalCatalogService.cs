using Experimento.Application.DTOs;

namespace Experimento.Application.Abstractions;

/// <summary>
/// Верифицированное вещество из внешнего каталога (PubChem).
/// </summary>
public record ChemicalDto(
    int PubChemCid,
    string Name,
    string? CasNumber,
    string? Formula,
    double MolarMass,
    string? Smiles);

/// <summary>
/// Кандидат автоподсказки. Если PubChemCid заполнен — вещество однозначно определено
/// (результат поиска по формуле/CAS или попадание в локальный каталог), и клиент может
/// резолвить его по CID. Если CID пуст — это вариант названия из автодополнения,
/// который резолвится по имени.
/// </summary>
public record ChemicalSuggestion(
    int? PubChemCid,
    string Name,
    string? Formula,
    string MatchType);

/// <summary>
/// Доступ к каталогу химических веществ: унифицированный поиск по названию,
/// CAS-номеру или молекулярной формуле, резолв вещества с кэшированием и
/// выборка по набору CID.
/// </summary>
public interface IChemicalCatalogService
{
    /// <summary>
    /// Подсказки для автодополнения. Понимает название/синоним, CAS-номер и
    /// молекулярную формулу; объединяет локальный каталог с результатами PubChem.
    /// Результаты кэшируются в памяти.
    /// </summary>
    Task<IReadOnlyList<ChemicalSuggestion>> SuggestAsync(string query, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Резолвит вещество по названию через PubChem и кэширует результат в БД.
    /// Возвращает null, если вещество не найдено.
    /// </summary>
    Task<ChemicalDto?> ResolveByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Резолвит конкретное вещество по PubChem CID (выбор из изомеров при поиске
    /// по формуле) и кэширует результат в БД.
    /// </summary>
    Task<ChemicalDto?> ResolveByCidAsync(int cid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает кэшированные записи каталога по набору CID (один запрос к БД).
    /// Используется для серверной верификации компонентов при создании версии.
    /// </summary>
    Task<IReadOnlyList<ChemicalDto>> GetByCidsAsync(IReadOnlyList<int> cids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Регуляторный статус вещества по всем подключенным спискам.
    /// Возвращает пустой список, если для вещества нет записей в справочнике.
    /// </summary>
    Task<ChemicalRegulationSummaryDto> GetRegulationsAsync(int cid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Регуляторные статусы для набора веществ одним запросом.
    /// Используется для проверки всей формуляции перед сохранением версии.
    /// </summary>
    Task<IReadOnlyList<ChemicalRegulationSummaryDto>> GetRegulationsBatchAsync(
        IReadOnlyList<int> cids, CancellationToken cancellationToken = default);
}
