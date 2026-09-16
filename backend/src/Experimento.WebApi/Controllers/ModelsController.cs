using Experimento.Application.Features.Models;
using Experimento.Application.Abstractions;
using Experimento.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Experimento.WebApi.Controllers;

public class ModelsController : BaseController
{
    public ModelsController(IMediator mediator, ICurrentUser currentUser, IAuditTrail audit)
        : base(mediator, currentUser, audit) { }

    [HttpGet]
    public async Task<IActionResult> List()
        => Ok(await Mediator.Send(new ListModelsQuery()));
}
