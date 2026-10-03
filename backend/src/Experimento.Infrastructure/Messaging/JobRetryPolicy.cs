namespace Experimento.Infrastructure.Messaging;

/// <summary>Общий предел повторов обработки задач и задержка перед новой доставкой.</summary>
public static class JobRetryPolicy
{
    public const int MaxAttempts = 3;

    public static TimeSpan Delay(int attempt) => TimeSpan.FromSeconds(5 * (1 << Math.Min(attempt - 1, 5)));
}
