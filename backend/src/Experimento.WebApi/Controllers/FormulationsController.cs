using Experimento.Application.Features.Formulations;
using Experimento.Application.Abstractions;
using Experimento.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Experimento.WebApi.Controllers;

public class FormulationsController : BaseController
{
    public FormulationsController(IMediator mediator, ICurrentUser currentUser, IAuditTrail audit)
        : base(mediator, currentUser, audit) { }

    [HttpGet("projects/{projectId:guid}")]
    public async Task<IActionResult> ListByProject(Guid projectId)
        => Ok(await Mediator.Send(new ListFormulationsByProjectQuery(projectId, UserId)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
        => Ok(await Mediator.Send(new ListVersionsQuery(id, UserId)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFormulationCommand cmd)
    {
        var result = await Mediator.Send(cmd with { UserId = UserId });
        await AuditAsync("Formulation.Create", "Formulation", result.Id.ToString(), cmd);
        return Ok(result);
    }

    [HttpPost("{formulationId:guid}/versions")]
    public async Task<IActionResult> CreateVersion(Guid formulationId, [FromBody] CreateVersionCommand cmd)
    {
        var result = await Mediator.Send(cmd with { FormulationId = formulationId, CreatedBy = UserId });
        await AuditAsync("FormulationVersion.Create", "FormulationVersion", result.Id.ToString(), cmd);
        return Ok(result);
    }

    [HttpGet("{formulationId:guid}/versions")]
    public async Task<IActionResult> ListVersions(Guid formulationId)
        => Ok(await Mediator.Send(new ListVersionsQuery(formulationId, UserId)));

    [HttpGet("versions/{versionId:guid}")]
    public async Task<IActionResult> GetVersion(Guid versionId)
        => Ok(await Mediator.Send(new GetVersionQuery(versionId, UserId)));

    [HttpGet("{formulationId:guid}/compare")]
    public async Task<IActionResult> Compare(Guid formulationId, [FromQuery] Guid a, [FromQuery] Guid b)
        => Ok(await Mediator.Send(new CompareVersionsQuery(formulationId, a, b, UserId)));

    /// <summary>
    /// Оценка готовности версии к масштабированию на целевой объём партии: теплоотвод,
    /// газовыделение, класс растворителя, pH и полнота данных. Скрининговый расчёт —
    /// перенос подтверждается термической калориметрией и пилотной партией.
    /// </summary>
    [HttpGet("versions/{versionId:guid}/scale-up")]
    public async Task<IActionResult> GetScaleUp(Guid versionId, [FromQuery] double targetVolumeLitres = 10)
        => Ok(await Mediator.Send(new GetScaleUpAssessmentQuery(versionId, targetVolumeLitres, UserId)));

    /// <summary>
    /// План следующих экспериментов по формуляции: лучшие непроверенные кандидаты симуляций,
    /// воспроизведение удачной версии и напоминания записать лабораторный исход. Уверенность
    /// рекомендации опирается на фактическую калибровку модели по внесённым исходам.
    /// </summary>
    [HttpGet("{formulationId:guid}/next-experiments")]
    public async Task<IActionResult> GetNextExperiments(Guid formulationId, [FromQuery] int limit = 5)
        => Ok(await Mediator.Send(new GetNextExperimentsQuery(formulationId, limit, UserId)));
}
