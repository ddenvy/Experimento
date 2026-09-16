using Experimento.Application.Abstractions;
using Experimento.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Experimento.WebApi.Security;

/// <summary>
/// Publishes job progress events to SignalR groups.
/// </summary>
public class SignalRJobNotifier : IJobNotifier
{
    private readonly IHubContext<JobsHub> _hub;
    public SignalRJobNotifier(IHubContext<JobsHub> hub) => _hub = hub;

    public Task PublishProgressAsync(string jobType, Guid jobId, int progress, string? stage, CancellationToken ct = default)
        => _hub.Clients.Group(JobsHub.GroupName(jobType, jobId))
            .SendAsync("progress", new { jobType, jobId, progress, stage }, ct);

    public Task PublishCompletedAsync(string jobType, Guid jobId, CancellationToken ct = default)
        => _hub.Clients.Group(JobsHub.GroupName(jobType, jobId))
            .SendAsync("completed", new { jobType, jobId }, ct);

    public Task PublishFaultedAsync(string jobType, Guid jobId, string error, CancellationToken ct = default)
        => _hub.Clients.Group(JobsHub.GroupName(jobType, jobId))
            .SendAsync("faulted", new { jobType, jobId, error }, ct);
}
