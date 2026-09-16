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
    private readonly IPropertyPredictor _predictor;
    private readonly IRationaleGenerator _rationale;
    private readonly IJobNotifier _notifier;
    private readonly ILogger<PredictionConsumer> _logger;

    public PredictionConsumer(AppDbContext db, IPropertyPredictor predictor, IRationaleGenerator rationale,
        IJobNotifier notifier, ILogger<PredictionConsumer> logger)
    {
        _db = db;
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
        if (await _db.PredictionResults.AnyAsync(r => r.JobId == jobId)) return; // результат уже создан при повторной доставке

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
            _db.PredictionResults.Add(result);
            await _db.SaveChangesAsync();

            job.Stage = "Generating rationale";
            job.Progress = 70;
            await _db.SaveChangesAsync();
            await _notifier.PublishProgressAsync("prediction", jobId, 70, "Generating rationale");

            var rationaleItems = await _rationale.GenerateAsync(snapshot, outcome, result.Id, context.CancellationToken);
            foreach (var item in rationaleItems)
                _db.RationaleItems.Add(item);
            await _db.SaveChangesAsync();

            job.Status = JobStatus.Completed;
            job.Progress = 100;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.Stage = "Completed";
            await _db.SaveChangesAsync();
            await _notifier.PublishCompletedAsync("prediction", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Prediction job {JobId} failed", jobId);
            job.Status = JobStatus.Failed;
            job.Error = ex.Message;
            job.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            // Наружу — обобщённое сообщение; детали исключения не раскрываем клиенту.
            await _notifier.PublishFaultedAsync("prediction", jobId, "Prediction failed. Please retry or contact support.");
        }
    }

    private static FormulationSnapshot MapToSnapshot(FormulationVersion version)
    {
        var components = version.Components.Select(c => new ComponentSnapshot(
            c.ChemicalName, c.CasNumber, c.Formula, c.MolarMass, c.Proportion, c.Role)).ToList();
        var conditions = new ConditionsSnapshot(
            version.Conditions.TemperatureCelsius, version.Conditions.PressureKPa,
            version.Conditions.PhTarget, version.Conditions.Solvent, version.Conditions.DeliveryTarget);
        return new FormulationSnapshot(version.Id, components, conditions, version.Formulation?.TargetPurpose ?? string.Empty);
    }
}
