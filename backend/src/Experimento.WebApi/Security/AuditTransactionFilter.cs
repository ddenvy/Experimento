using Experimento.Infrastructure.Data;
using Experimento.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Experimento.WebApi.Security;

/// <summary>Помечает действие, запись аудита которого обязательна для коммита.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AtomicAuditAttribute : Attribute { }

/// <summary>Фиксирует бизнес-изменение и запись аудита одной транзакцией.</summary>
public sealed class AuditTransactionFilter : IAsyncActionFilter
{
    private readonly AppDbContext _db;
    public AuditTransactionFilter(AppDbContext db) => _db = db;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.Controller is not BaseController ||
            context.ActionDescriptor is not ControllerActionDescriptor action ||
            !Attribute.IsDefined(action.MethodInfo, typeof(AtomicAuditAttribute)))
        {
            await next();
            return;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(context.HttpContext.RequestAborted);
        var result = await next();
        var status = (result.Result as IStatusCodeActionResult)?.StatusCode;
        if (result.Exception is null && (status is null || status < 400))
            await tx.CommitAsync(context.HttpContext.RequestAborted);
    }
}
