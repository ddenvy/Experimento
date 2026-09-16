using Experimento.Application.Features.Projects;
using Experimento.Application.Abstractions;
using Experimento.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Experimento.WebApi.Controllers;

public class ProjectsController : BaseController
{
    public ProjectsController(IMediator mediator, ICurrentUser currentUser, IAuditTrail audit)
        : base(mediator, currentUser, audit) { }

    [HttpGet]
    public async Task<IActionResult> List()
        => Ok(await Mediator.Send(new ListProjectsQuery(UserId)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
        => Ok(await Mediator.Send(new GetProjectQuery(id, UserId)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectCommand cmd)
    {
        var result = await Mediator.Send(cmd with { CreatedBy = UserId });
        await AuditAsync("Project.Create", "Project", result.Id.ToString(), cmd);
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProjectCommand cmd)
    {
        var result = await Mediator.Send(cmd with { Id = id, UserId = UserId });
        await AuditAsync("Project.Update", "Project", id.ToString(), cmd);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await Mediator.Send(new DeleteProjectCommand(id, UserId));
        await AuditAsync("Project.Delete", "Project", id.ToString());
        return NoContent();
    }
}
