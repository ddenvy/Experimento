namespace Experimento.Domain.Enums;

/// <summary>
/// Async job status shared by predictions and simulations.
/// </summary>
public enum JobStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}
