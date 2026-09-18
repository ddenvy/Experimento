using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Experimento.Infrastructure.Data;

namespace Experimento.Infrastructure.Messaging;

/// <summary>
/// Consumes prediction jobs: runs the predictor, generates rationale, saves result.
/// </summary>
public class PredictionConsumer : IConsumer<SubmitPredictionCommand>
{
    private readonly AppDbContext _db;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IPropertyPredictor _predictor;
    private readonly IRationaleGenerator _rationale;
    private readonly IJobNotifier _notifier;
    private readonly ILogger<PredictionConsumer> _logger;

    public PredictionConsumer(AppDbContext db, IDbContextFactory<AppDbContext> dbFactory,
        IPropertyPredictor predictor, IRationaleGenerator rationale,
        IJobNotifier notifier, ILogger<PredictionConsumer> logger)
    {
        _db = db;
        _dbFactory = dbFactory;
        _predictor = predictor;
        _rationale = rationale;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SubmitPredictionCommand> context)
    {
        var jobId = context.Message.JobId;
        var job = await _db.PredictionJobs.FindAsync([jobId]);
        if (job is null || job.Status == JobStatus.Completed) return; // идемпотентность

        try
        {
            job.Status = JobStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;
            job.Stage = "Loading formulation";
            job.Progress = 10;
            await _db.SaveChangesAsync();
            await _notifier.PublishProgressAsync("prediction", jobId, 10, "Loading formulation");

            var version = await _db.FormulationVersions
                .Include(v => v.Components)
                .FirstOrDefaultAsync(v => v.Id == job.VersionId)
                ?? throw new InvalidOperationException($"Version {job.VersionId} not found.");

            var snapshot = MapToSnapshot(version);

            job.Stage = "Running prediction model";
            job.Progress = 40;
            await _db.SaveChangesAsync();
            await _notifier.PublishProgressAsync("prediction", jobId, 40, "Running prediction model");

            var outcome = await _predictor.PredictAsync(snapshot, context.CancellationToken);

            // Find model registration for the predictor
            var model = await _db.ModelRegistrations
                .FirstOrDefaultAsync(m => m.Name == _predictor.ModelName && m.Version == _predictor.ModelVersion)
                ?? throw new InvalidOperationException("Predictor model not registered.");

            job.Stage = "Generating rationale";
            job.Progress = 70;
            await _db.SaveChangesAsync();
            await _notifier.PublishProgressAsync("prediction", jobId, 70, "Generating rationale");

            // Результат сохраняем короткой транзакцией ДО вызова LLM: он нужен как ResultId
            // для rationale, а держать соединение с БД на время сетевого вызова нельзя.
            var result = new PredictionResult
            {
                JobId = jobId,
                ModelRegistrationId = model.Id,
                SuccessProbability = outcome.SuccessProbability,
                ToxicityScore = outcome.ToxicityScore,
                StabilityScore = outcome.StabilityScore,
                SideRiskLevel = (SideRiskLevel)outcome.SideRiskLevel,
                Summary = outcome.Summary
            };
            var existingResultId = await _db.PredictionResults
                .Where(r => r.JobId == jobId)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(context.CancellationToken);
            if (existingResultId is { } resumedResultId)
            {
                // Повторная доставка после сбоя между сохранением результата и финализацией.
                result.Id = resumedResultId;
            }
            else
            {
                _db.PredictionResults.Add(result);
                await _db.SaveChangesAsync(context.CancellationToken);
            }

            // Внешний вызов LLM — вне транзакции.
            var rationaleItems = await _rationale.GenerateAsync(snapshot, outcome, result.Id, context.CancellationToken);

            // Обоснование и финальный статус — атомарно. Раньше это были отдельные
            // SaveChanges: сбой посередине оставлял job в Running навсегда.
            await using var tx = await _db.Database.BeginTransactionAsync(context.CancellationToken);

            // На случай частично записанных items при прошлой попытке — пишем идемпотентно.
            await _db.RationaleItems
                .Where(i => i.ResultId == result.Id)
                .ExecuteDeleteAsync(context.CancellationToken);
            foreach (var item in rationaleItems)
                _db.RationaleItems.Add(item);

            job.Status = JobStatus.Completed;
            job.Progress = 100;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.Stage = "Completed";
            await _db.SaveChangesAsync(context.CancellationToken);
            await tx.CommitAsync(context.CancellationToken);

            await _notifier.PublishCompletedAsync("prediction", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Prediction job {JobId} failed", jobId);
            await MarkJobFailedAsync(jobId, ex);
            // Наружу — обобщённое сообщение; детали исключения не раскрываем клиенту.
            await _notifier.PublishFaultedAsync("prediction", jobId, "Prediction failed. Please retry or contact support.");
        }
    }

    /// <summary>
    /// Помечает job как Failed через ОТДЕЛЬНЫЙ контекст: если упал сам SaveChanges,
    /// исходный контекст может быть в нерабочем состоянии, и повторная запись в него
    /// бросила бы второе исключение, оставив job в Running.
    /// </summary>
    private async Task MarkJobFailedAsync(Guid jobId, Exception ex)
    {
        try
        {
            await using var errorDb = await _dbFactory.CreateDbContextAsync();
            await errorDb.PredictionJobs
                .Where(j => j.Id == jobId && j.Status != JobStatus.Completed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, JobStatus.Failed)
                    .SetProperty(j => j.Error, ex.Message.Length > 4000 ? ex.Message[..4000] : ex.Message)
                    .SetProperty(j => j.CompletedAtUtc, DateTime.UtcNow));
        }
        catch (Exception persistEx)
        {
            // Даже пометить Failed не удалось — пусть сообщение уйдёт в retry-очередь MassTransit.
            _logger.LogCritical(persistEx, "Failed to mark prediction job {JobId} as Failed", jobId);
            throw;
        }
    }

    private static FormulationSnapshot MapToSnapshot(FormulationVersion version)
    {
        var components = version.Components.Select(c => new ComponentSnapshot(
            c.ChemicalName, c.CasNumber, c.Formula, c.MolarMass, c.Proportion, c.Role, c.Smiles)).ToList();
        var conditions = new ConditionsSnapshot(
            version.Conditions.TemperatureCelsius, version.Conditions.PressureKPa,
            version.Conditions.PhTarget, version.Conditions.Solvent, version.Conditions.DeliveryTarget);
        return new FormulationSnapshot(version.Id, components, conditions, version.Formulation?.TargetPurpose ?? string.Empty);
    }
}
