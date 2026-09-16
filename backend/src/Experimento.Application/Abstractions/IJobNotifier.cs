namespace Experimento.Application.Abstractions;

/// <summary>
/// Publishes job progress to real-time subscribers (SignalR).
/// </summary>
public interface IJobNotifier
{
    Task PublishProgressAsync(string jobType, Guid jobId, int progress, string? stage, CancellationToken ct = default);
    Task PublishCompletedAsync(string jobType, Guid jobId, CancellationToken ct = default);
    Task PublishFaultedAsync(string jobType, Guid jobId, string error, CancellationToken ct = default);
}
