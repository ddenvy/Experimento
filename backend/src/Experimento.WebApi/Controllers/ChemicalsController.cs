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

    public ChemicalsController(IChemicalCatalogService catalog) => _catalog = catalog;

    /// <summary>Автоподсказки названий веществ (для выпадающего списка в UI).</summary>
    [HttpGet("suggest")]
    public async Task<IActionResult> Suggest([FromQuery] string? query, [FromQuery] int limit = 8,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            throw new BadRequestException("Query must be at least 2 characters.");
        var names = await _catalog.SuggestNamesAsync(query, limit, cancellationToken);
        return Ok(names);
    }

    /// <summary>
    /// Резолвит вещество по названию: возвращает CID, формулу, молекулярную массу и CAS
    /// и кэширует результат. Вызывается при выборе вещества из списка подсказок.
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
}
