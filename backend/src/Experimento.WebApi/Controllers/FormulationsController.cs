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
}
