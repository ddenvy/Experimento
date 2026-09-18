using Experimento.Application.Abstractions;
using Experimento.Application.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Experimento.WebApi.Controllers;

/// <summary>
/// Поиск и резолв веществ в каталоге PubChem с серверным кэшированием.
/// </summary>
[Authorize]
[ApiController]
[Route("api/chemicals")]
public class ChemicalsController : ControllerBase
{
    private readonly IChemicalCatalogService _catalog;
    private readonly ISubstituteFinder _substitutes;

    public ChemicalsController(IChemicalCatalogService catalog, ISubstituteFinder substitutes)
        => (_catalog, _substitutes) = (catalog, substitutes);

    /// <summary>
    /// Автоподсказки для одного поля поиска: название/синоним, CAS-номер
    /// (50-78-2) или молекулярная формула (C9H8O4, H2O). Для формулы возвращается
    /// список изомеров с CID — конкретное вещество выбирает пользователь.
    /// </summary>
    [HttpGet("suggest")]
    public async Task<IActionResult> Suggest([FromQuery] string? query, [FromQuery] int limit = 8,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            throw new BadRequestException("Query must be at least 2 characters.");
        var suggestions = await _catalog.SuggestAsync(query, limit, cancellationToken);
        return Ok(suggestions);
    }

    /// <summary>
    /// Резолвит вещество по названию: возвращает CID, формулу, молекулярную массу и CAS
    /// и кэширует результат. Вызывается при выборе варианта автодополнения по названию.
    /// </summary>
    [HttpGet("resolve")]
    public async Task<IActionResult> Resolve([FromQuery] string? name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2)
            throw new BadRequestException("Name must be at least 2 characters.");
        var chemical = await _catalog.ResolveByNameAsync(name, cancellationToken);
        if (chemical is null)
            throw new NotFoundException($"Chemical '{name}' was not found in PubChem.");
        return Ok(chemical);
    }

    /// <summary>
    /// Резолвит вещество по PubChem CID (выбор конкретного изомера из результатов
    /// поиска по формуле или CAS).
    /// </summary>
    [HttpGet("resolve-cid/{cid:int}")]
    public async Task<IActionResult> ResolveByCid(int cid, CancellationToken cancellationToken)
    {
        var chemical = await _catalog.ResolveByCidAsync(cid, cancellationToken);
        if (chemical is null)
            throw new NotFoundException($"Chemical with CID {cid} was not found in PubChem.");
        return Ok(chemical);
    }

    /// <summary>
    /// Регуляторный статус вещества по всем подключенным спискам (REACH SVHC, EPA PFAS, Prop 65, VOC и др.).
    /// Возвращает пустой список, если для вещества нет записей в справочнике.
    /// </summary>
    [HttpGet("regulations/{cid:int}")]
    public async Task<IActionResult> GetRegulations(int cid, CancellationToken cancellationToken)
    {
        var summary = await _catalog.GetRegulationsAsync(cid, cancellationToken);
        return Ok(summary);
    }

    /// <summary>
    /// Пакетное получение регуляторных статусов по списку CID.
    /// </summary>
    [HttpPost("regulations/batch")]
    public async Task<IActionResult> GetRegulationsBatch([FromBody] int[] cids, CancellationToken cancellationToken)
    {
        if (cids is null || cids.Length == 0) return Ok(Array.Empty<object>());
        var result = await _catalog.GetRegulationsBatchAsync(cids, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Подбор замен вещества из каталога: ближайшие по составу, массе и классу опасности
    /// аналоги с указанием их регуляторного статуса. Помогает найти альтернативу при
    /// перебоях поставок. Эквивалентность подтверждается экспериментом.
    /// </summary>
    [HttpGet("{cid:int}/substitutes")]
    public async Task<IActionResult> GetSubstitutes(int cid, [FromQuery] int limit = 8,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 25)
            throw new BadRequestException("Limit must be between 1 and 25.");
        var candidates = await _substitutes.FindAsync(cid, limit, cancellationToken);
        if (candidates is null)
            throw new NotFoundException($"Chemical with CID {cid} is not in the catalog.");
        return Ok(candidates);
    }
}
