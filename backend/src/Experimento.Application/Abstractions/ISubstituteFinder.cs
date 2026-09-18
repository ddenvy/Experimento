using System.Text.Json.Serialization;

namespace Experimento.Application.Abstractions;

/// <summary>
/// Кандидат на замену компонента формуляции.
/// <see cref="Similarity"/> — чистая близость свойств (0..1),
/// <see cref="MatchScore"/> — она же с поправкой на регуляторный статус, по ней идёт сортировка.
/// Регуляторный статус отдаётся строкой ("Banned") — фронтенд сопоставляет по имени, а не по числу.
/// </summary>
public record SubstituteCandidateDto(
    int PubChemCid,
    string Name,
    string? CasNumber,
    string? Formula,
    double MolarMass,
    double Similarity,
    double MatchScore,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] RegulationStatus RegulatoryStatus,
    IReadOnlyList<string> MatchedSignals);

/// <summary>
/// Подбор замен компонента по данным каталога: элементный состав, молярная масса
/// и класс опасности. Инструмент поиска альтернатив при перебоях поставок,
/// а не доказательство эквивалентности — замену подтверждают экспериментом.
/// </summary>
public interface ISubstituteFinder
{
    /// <summary>
    /// Возвращает ранжированных кандидатов на замену вещества.
    /// null — если само вещество отсутствует в каталоге.
    /// </summary>
    Task<IReadOnlyList<SubstituteCandidateDto>?> FindAsync(int pubChemCid, int limit,
        CancellationToken cancellationToken = default);
}
