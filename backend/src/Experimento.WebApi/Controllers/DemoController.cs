using Experimento.Application.Abstractions;
using Experimento.Application.Features.Demo;
using Experimento.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Experimento.WebApi.Controllers;

/// <summary>
/// Demo-данные: provision готового проекта с полным циклом R&D для новых пользователей
/// или для быстрого ознакомления с продуктом.
/// </summary>
[Authorize]
[ApiController]
[Route("api/demo")]
public class DemoController : BaseController
{
    public DemoController(IMediator mediator, ICurrentUser currentUser, IAuditTrail audit)
        : base(mediator, currentUser, audit)
    {
    }

    /// <summary>
    /// Создаёт demo-проект с полным научным циклом для текущего пользователя.
    /// Идемпотентно: если demo уже есть — возвращает существующий.
    /// </summary>
    [HttpPost("provision")]
    public async Task<IActionResult> Provision(CancellationToken ct)
    {
        var result = await Mediator.Send(new ProvisionDemoDataCommand(UserId), ct);
        await AuditAsync("Demo.Provision", "Project", result.ProjectId.ToString());
        return Ok(result);
    }
}
