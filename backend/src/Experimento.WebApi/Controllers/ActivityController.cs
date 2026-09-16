using Experimento.Application.Abstractions;
using Experimento.Application.Features.Activity;
using Experimento.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Experimento.WebApi.Controllers;

/// <summary>
/// Агрегированная лента активности пользователя для командного центра.
/// </summary>
public class ActivityController : BaseController
{
    public ActivityController(IMediator mediator, ICurrentUser currentUser, IAuditTrail audit)
        : base(mediator, currentUser, audit) { }

    [HttpGet("recent-runs")]
    public async Task<IActionResult> RecentRuns()
        => Ok(await Mediator.Send(new GetRecentRunsQuery(UserId)));
}
