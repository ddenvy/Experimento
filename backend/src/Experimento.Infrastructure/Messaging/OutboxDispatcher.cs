using System.Text.Json;
using Experimento.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Experimento.Infrastructure.Messaging;

/// <summary>Отправляет сохраненные сообщения с повтором после сбоя брокера.</summary>
public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(IServiceScopeFactory scopeFactory, ILogger<OutboxDispatcher> logger)
        => (_scopeFactory, _logger) = (scopeFactory, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await DispatchBatchAsync(stoppingToken) > 0)
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox dispatch cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var now = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var messages = await db.OutboxMessages.FromSqlInterpolated($@"
            SELECT * FROM ""OutboxMessages""
            WHERE ""PublishedAtUtc"" IS NULL AND ""AvailableAtUtc"" <= {now}
              AND (""LeaseUntilUtc"" IS NULL OR ""LeaseUntilUtc"" < {now})
            ORDER BY ""CreatedAtUtc"", ""Id""
            LIMIT {1} FOR UPDATE SKIP LOCKED").ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            message.LeaseId = Guid.NewGuid();
            message.LeaseUntilUtc = now.AddSeconds(45);
            message.AttemptCount++;
        }
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                await PublishAsync(publisher, message, timeout.Token);
                await db.OutboxMessages.Where(m => m.Id == message.Id && m.LeaseId == message.LeaseId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.PublishedAtUtc, DateTime.UtcNow)
                        .SetProperty(m => m.LeaseId, (Guid?)null)
                        .SetProperty(m => m.LeaseUntilUtc, (DateTime?)null)
                        .SetProperty(m => m.LastError, (string?)null), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await db.OutboxMessages.Where(m => m.Id == message.Id && m.LeaseId == message.LeaseId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(m => m.LeaseId, (Guid?)null)
                            .SetProperty(m => m.LeaseUntilUtc, (DateTime?)null), CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not release outbox lease for {MessageId}", message.Id);
                }
                throw;
            }
            catch (Exception ex)
            {
                var seconds = Math.Min(300, 1 << Math.Min(message.AttemptCount, 8));
                var error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                await db.OutboxMessages.Where(m => m.Id == message.Id && m.LeaseId == message.LeaseId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.AvailableAtUtc, DateTime.UtcNow.AddSeconds(seconds))
                        .SetProperty(m => m.LeaseId, (Guid?)null)
                        .SetProperty(m => m.LeaseUntilUtc, (DateTime?)null)
                        .SetProperty(m => m.LastError, error), cancellationToken);
                _logger.LogWarning(ex, "Outbox message {MessageId} will be retried", message.Id);
            }
        }

        return messages.Count;
    }

    private static Task PublishAsync(IPublishEndpoint publisher, OutboxMessage message, CancellationToken ct)
        => message.Kind switch
        {
            OutboxKinds.Prediction => publisher.Publish(
                JsonSerializer.Deserialize<SubmitPredictionCommand>(message.PayloadJson)
                ?? throw new InvalidOperationException("Empty prediction command"), ct),
            OutboxKinds.Simulation => publisher.Publish(
                JsonSerializer.Deserialize<SubmitSimulationCommand>(message.PayloadJson)
                ?? throw new InvalidOperationException("Empty simulation command"), ct),
            OutboxKinds.Document => publisher.Publish(
                JsonSerializer.Deserialize<IngestDocumentCommand>(message.PayloadJson)
                ?? throw new InvalidOperationException("Empty document command"), ct),
            _ => throw new InvalidOperationException($"Unknown outbox kind: {message.Kind}")
        };
}
