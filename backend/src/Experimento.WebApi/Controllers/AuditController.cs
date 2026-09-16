using Experimento.Application.Features.Audit;
using Experimento.Application.Abstractions;
using Experimento.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Experimento.WebApi.Controllers;

/// <summary>
/// Журнал аудита и проверка целостности цепочки — комплаенс-функция только для администраторов.
/// </summary>
[Authorize(Roles = "Admin")]
public class AuditController : BaseController
{
    public AuditController(IMediator mediator, ICurrentUser currentUser, IAuditTrail audit)
        : base(mediator, currentUser, audit) { }

    [HttpGet]
    public async Task<IActionResult> Trail([FromQuery] string? entityType, [FromQuery] string? entityId,
        [FromQuery] int skip = 0, [FromQuery] int take = 50)
        => Ok(await Mediator.Send(new GetAuditTrailQuery(entityType, entityId, skip, take)));

    [HttpGet("verify")]
    public async Task<IActionResult> Verify()
    {
        var (isIntact, brokenId) = await Mediator.Send(new VerifyAuditIntegrityQuery());
        return Ok(new { isIntact, firstBrokenId = brokenId });
    }
}
