using Experimento.Application.Features.Knowledge;
using Experimento.Application.Abstractions;
using Experimento.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Experimento.WebApi.Controllers;

public class KnowledgeController : BaseController
{
    public KnowledgeController(IMediator mediator, ICurrentUser currentUser, IAuditTrail audit)
        : base(mediator, currentUser, audit) { }

    [HttpPost("documents")]
    public async Task<IActionResult> Upload([FromBody] UploadDocumentCommand cmd)
    {
        var result = await Mediator.Send(cmd with { UploadedBy = UserId });
        await AuditAsync("Knowledge.Upload", "KnowledgeDocument", result.Id.ToString(), cmd);
        return Ok(result);
    }

    [HttpGet("documents")]
    public async Task<IActionResult> List([FromQuery] Guid? projectId)
        => Ok(await Mediator.Send(new ListDocumentsQuery(projectId, UserId)));

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SearchKnowledgeQuery query)
        => Ok(await Mediator.Send(query with { UserId = UserId }));
}
