using System.Text.Json;
using Experimento.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Experimento.Infrastructure.Messaging;

/// <summary>Возвращает в очередь задачи, оставшиеся в работе после сбоя процесса.</summary>
public sealed class StalledJobRecovery : BackgroundService
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PendingAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RecoveryDelay = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StalledJobRecovery> _logger;

    public StalledJobRecovery(IServiceScopeFactory scopeFactory, ILogger<StalledJobRecovery> logger)
        => (_scopeFactory, _logger) = (scopeFactory, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecoverAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stalled job recovery failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTime.UtcNow.Subtract(StaleAfter);
        var pendingCutoff = DateTime.UtcNow.Subtract(PendingAfter);

        var predictionIds = await db.PredictionJobs.AsNoTracking()
            .Where(j => (j.Status == JobStatus.Running && (j.StartedAtUtc ?? j.CreatedAtUtc) < cutoff) ||
                (j.Status == JobStatus.Pending && j.CreatedAtUtc < pendingCutoff &&
                 !db.OutboxMessages.Any(m => m.Kind == OutboxKinds.Prediction && m.EntityId == j.Id &&
                     (m.PublishedAtUtc == null || m.CreatedAtUtc >= pendingCutoff))))
            .OrderBy(j => j.CreatedAtUtc).Take(32).Select(j => j.Id).ToListAsync(cancellationToken);
        foreach (var id in predictionIds)
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var rows = await db.PredictionJobs
                .FromSqlInterpolated($@"SELECT * FROM ""PredictionJobs"" WHERE ""Id"" = {id} FOR UPDATE SKIP LOCKED")
                .ToListAsync(cancellationToken);
            var job = rows.SingleOrDefault();
            var recover = job is not null &&
                ((job.Status == JobStatus.Running && (job.StartedAtUtc ?? job.CreatedAtUtc) < cutoff) ||
                 (job.Status == JobStatus.Pending && job.CreatedAtUtc < pendingCutoff &&
                  !await db.OutboxMessages.AnyAsync(m => m.Kind == OutboxKinds.Prediction && m.EntityId == id &&
                      (m.PublishedAtUtc == null || m.CreatedAtUtc >= pendingCutoff), cancellationToken)));
            if (recover && job is not null)
            {
                if (job.Status == JobStatus.Running)
                    job.AttemptCount++;
                if (job.AttemptCount >= JobRetryPolicy.MaxAttempts)
                {
                    job.Status = JobStatus.Failed;
                    job.Error = "Processing timed out after repeated attempts.";
                    job.CompletedAtUtc = DateTime.UtcNow;
                }
                else
                {
                    job.Status = JobStatus.Pending;
                    job.StartedAtUtc = null;
                    job.Progress = 0;
                    job.Stage = "Retry scheduled";
                    db.OutboxMessages.Add(OutboxMessage.Create(OutboxKinds.Prediction, id,
                        JsonSerializer.Serialize(new SubmitPredictionCommand(id)),
                        DateTime.UtcNow.Add(RecoveryDelay)));
                }
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogWarning("Recovered stalled prediction job {JobId}", id);
            }
            await tx.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }

        var simulationIds = await db.SimulationJobs.AsNoTracking()
            .Where(j => (j.Status == JobStatus.Running && (j.StartedAtUtc ?? j.CreatedAtUtc) < cutoff) ||
                (j.Status == JobStatus.Pending && j.CreatedAtUtc < pendingCutoff &&
                 !db.OutboxMessages.Any(m => m.Kind == OutboxKinds.Simulation && m.EntityId == j.Id &&
                     (m.PublishedAtUtc == null || m.CreatedAtUtc >= pendingCutoff))))
            .OrderBy(j => j.CreatedAtUtc).Take(32).Select(j => j.Id).ToListAsync(cancellationToken);
        foreach (var id in simulationIds)
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var rows = await db.SimulationJobs
                .FromSqlInterpolated($@"SELECT * FROM ""SimulationJobs"" WHERE ""Id"" = {id} FOR UPDATE SKIP LOCKED")
                .ToListAsync(cancellationToken);
            var job = rows.SingleOrDefault();
            var recover = job is not null &&
                ((job.Status == JobStatus.Running && (job.StartedAtUtc ?? job.CreatedAtUtc) < cutoff) ||
                 (job.Status == JobStatus.Pending && job.CreatedAtUtc < pendingCutoff &&
                  !await db.OutboxMessages.AnyAsync(m => m.Kind == OutboxKinds.Simulation && m.EntityId == id &&
                      (m.PublishedAtUtc == null || m.CreatedAtUtc >= pendingCutoff), cancellationToken)));
            if (recover && job is not null)
            {
                if (job.Status == JobStatus.Running)
                    job.AttemptCount++;
                if (job.AttemptCount >= JobRetryPolicy.MaxAttempts)
                {
                    job.Status = JobStatus.Failed;
                    job.Error = "Processing timed out after repeated attempts.";
                    job.CompletedAtUtc = DateTime.UtcNow;
                }
                else
                {
                    job.Status = JobStatus.Pending;
                    job.StartedAtUtc = null;
                    job.Progress = 0;
                    db.OutboxMessages.Add(OutboxMessage.Create(OutboxKinds.Simulation, id,
                        JsonSerializer.Serialize(new SubmitSimulationCommand(id)),
                        DateTime.UtcNow.Add(RecoveryDelay)));
                }
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogWarning("Recovered stalled simulation job {JobId}", id);
            }
            await tx.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }

        var documentIds = await db.KnowledgeDocuments.AsNoTracking()
            .Where(d => (d.Status == KnowledgeStatus.Processing && (d.StartedAtUtc ?? d.UploadedAtUtc) < cutoff) ||
                (d.Status == KnowledgeStatus.Pending && d.UploadedAtUtc < pendingCutoff &&
                 !db.OutboxMessages.Any(m => m.Kind == OutboxKinds.Document && m.EntityId == d.Id &&
                     (m.PublishedAtUtc == null || m.CreatedAtUtc >= pendingCutoff))))
            .OrderBy(d => d.UploadedAtUtc).Take(32).Select(d => d.Id).ToListAsync(cancellationToken);
        foreach (var id in documentIds)
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var rows = await db.KnowledgeDocuments
                .FromSqlInterpolated($@"SELECT * FROM ""KnowledgeDocuments"" WHERE ""Id"" = {id} FOR UPDATE SKIP LOCKED")
                .ToListAsync(cancellationToken);
            var doc = rows.SingleOrDefault();
            var recover = doc is not null &&
                ((doc.Status == KnowledgeStatus.Processing && (doc.StartedAtUtc ?? doc.UploadedAtUtc) < cutoff) ||
                 (doc.Status == KnowledgeStatus.Pending && doc.UploadedAtUtc < pendingCutoff &&
                  !await db.OutboxMessages.AnyAsync(m => m.Kind == OutboxKinds.Document && m.EntityId == id &&
                      (m.PublishedAtUtc == null || m.CreatedAtUtc >= pendingCutoff), cancellationToken)));
            if (recover && doc is not null)
            {
                var payload = await db.OutboxMessages.AsNoTracking()
                    .Where(m => m.Kind == OutboxKinds.Document && m.EntityId == id)
                    .OrderByDescending(m => m.CreatedAtUtc).Select(m => m.PayloadJson)
                    .FirstOrDefaultAsync(cancellationToken);
                if (doc.Status == KnowledgeStatus.Processing)
                    doc.AttemptCount++;
                if (doc.AttemptCount >= JobRetryPolicy.MaxAttempts || payload is null)
                {
                    doc.Status = KnowledgeStatus.Failed;
                }
                else
                {
                    doc.Status = KnowledgeStatus.Pending;
                    doc.StartedAtUtc = null;
                    db.OutboxMessages.Add(OutboxMessage.Create(OutboxKinds.Document, id, payload,
                        DateTime.UtcNow.Add(RecoveryDelay)));
                }
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogWarning("Recovered stalled document {DocumentId}", id);
            }
            await tx.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }
}
