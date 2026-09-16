using System.Security.Claims;
using Experimento.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Experimento.WebApi.Hubs;

/// <summary>
/// SignalR hub broadcasting job progress to subscribed clients.
/// Clients join a group named "{jobType}:{jobId}" to receive events.
/// Подписка на чужую задачу запрещена: проверяем владение через ResourceAuthorization.
/// </summary>
[Authorize]
public class JobsHub(ResourceAuthorization auth) : Hub
{
    public async Task SubscribeToJob(string jobType, Guid jobId)
    {
        var userId = GetUserId();
        var owns = jobType switch
        {
            "prediction" => await auth.OwnsPredictionJobAsync(jobId, userId, Context.ConnectionAborted),
            "simulation" => await auth.OwnsSimulationJobAsync(jobId, userId, Context.ConnectionAborted),
            _ => false
        };
        if (!owns)
            throw new HubException("Job not found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(jobType, jobId));
    }

    public async Task UnsubscribeFromJob(string jobType, Guid jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(jobType, jobId));
    }

    private Guid GetUserId()
    {
        var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? Context.User?.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    public static string GroupName(string jobType, Guid jobId) => $"{jobType}:{jobId}";
}
