using System.Text.Json;
using Experimento.Application.Abstractions;
using Experimento.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Experimento.WebApi.Controllers;

/// <summary>
/// Base controller providing MediatR, current user, and audit helpers.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public abstract class BaseController : ControllerBase
{
    protected IMediator Mediator { get; }
    protected ICurrentUser CurrentUser { get; }
    private readonly IAuditTrail _audit;

    protected BaseController(IMediator mediator, ICurrentUser currentUser, IAuditTrail audit)
    {
        Mediator = mediator;
        CurrentUser = currentUser;
        _audit = audit;
    }

    protected Guid UserId => CurrentUser.UserId;

    protected async Task AuditAsync(string action, string entityType, string? entityId, object? payload = null)
    {
        var payloadJson = payload is null ? "{}" : JsonSerializer.Serialize(payload);
        await _audit.AppendAsync(UserId, action, entityType, entityId, payloadJson);
    }
}
