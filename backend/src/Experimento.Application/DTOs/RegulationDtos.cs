using System.Text.Json.Serialization;
using Experimento.Domain.Enums;

namespace Experimento.Application.DTOs;

/// <summary>
/// Регуляторный статус вещества по одному органу/списку.
/// Enum'ы отдаются строками ("Banned", "ReachSvhc") — читаемо в API и не ломается
/// при изменении порядка значений в enum.
/// </summary>
public record ChemicalRegulationDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] RegulationAuthority Authority,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] RegulationStatus Status,
    string Reason,
    string? SourceUrl);

/// <summary>
/// Агрегированный регуляторный обзор вещества: наивысший статус + детали по каждому органу.
/// </summary>
public record ChemicalRegulationSummaryDto(
    int PubChemCid,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] RegulationStatus HighestStatus,
    IReadOnlyList<ChemicalRegulationDto> Regulations);
