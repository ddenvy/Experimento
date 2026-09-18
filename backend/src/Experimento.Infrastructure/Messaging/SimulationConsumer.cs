using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Experimento.Infrastructure.Data;
using Experimento.Infrastructure.Simulations;

namespace Experimento.Infrastructure.Messaging;

/// <summary>
/// Consumes simulation jobs: runs the stress-test engine and persists ranked candidates.
/// </summary>
public class SimulationConsumer : IConsumer<SubmitSimulationCommand>
{
    private readonly AppDbContext _db;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly SimulationEngine _engine;
    private readonly IJobNotifier _notifier;
    private readonly ILogger<SimulationConsumer> _logger;

    public SimulationConsumer(AppDbContext db, IDbContextFactory<AppDbContext> dbFactory,
        SimulationEngine engine, IJobNotifier notifier, ILogger<SimulationConsumer> logger)
    {
        _db = db;
        _dbFactory = dbFactory;
        _engine = engine;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SubmitSimulationCommand> context)
    {
        var jobId = context.Message.JobId;
        var job = await _db.SimulationJobs.FindAsync([jobId]);
        if (job is null || job.Status == JobStatus.Completed) return;
        if (await _db.SimulationResults.AnyAsync(r => r.JobId == jobId)) return;

        try
        {
            job.Status = JobStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;
            job.Progress = 5;
            await _db.SaveChangesAsync();
            await _notifier.PublishProgressAsync("simulation", jobId, 5, "Loading formulation");

            var version = await _db.FormulationVersions
                .Include(v => v.Components)
                .FirstOrDefaultAsync(v => v.Id == job.VersionId)
                ?? throw new InvalidOperationException($"Version {job.VersionId} not found.");

            var snapshot = new FormulationSnapshot(
                version.Id,
                version.Components.Select(c => new ComponentSnapshot(c.ChemicalName, c.CasNumber, c.Formula, c.MolarMass, c.Proportion, c.Role, c.Smiles)).ToList(),
                new ConditionsSnapshot(version.Conditions.TemperatureCelsius, version.Conditions.PressureKPa,
                    version.Conditions.PhTarget, version.Conditions.Solvent, version.Conditions.DeliveryTarget),
                version.Formulation?.TargetPurpose ?? string.Empty);

            var runResult = await _engine.RunAsync(jobId, snapshot, job.ConfigJson, context.CancellationToken);

            // Результат, кандидаты и финальный статус — одной транзакцией. Раньше это были
            // четыре отдельных SaveChanges, и сбой посередине оставлял result без bestCandidate
            // и job в Running; повторная доставка при этом рано выходила по наличию result.
            await using var tx = await _db.Database.BeginTransactionAsync(context.CancellationToken);

            var result = new SimulationResult
            {
                JobId = jobId,
                Summary = runResult.Summary,
                IterationsExecuted = runResult.IterationsExecuted
            };
            _db.SimulationResults.Add(result);
            await _db.SaveChangesAsync(context.CancellationToken);

            // Persist top 50 candidates
            var addedCandidates = new List<SimulationCandidate>();
            foreach (var c in runResult.RankedCandidates.Take(50))
            {
                var entity = new SimulationCandidate
                {
                    ResultId = result.Id,
                    ParametersJson = c.ParametersJson,
                    SuccessProbability = c.SuccessProbability,
                    Score = c.Score,
                    Rank = c.Rank
                };
                _db.SimulationCandidates.Add(entity);
                addedCandidates.Add(entity);
            }
            await _db.SaveChangesAsync(context.CancellationToken);

            result.BestCandidateId = addedCandidates.FirstOrDefault(c => c.Rank == 1)?.Id;

            job.Status = JobStatus.Completed;
            job.Progress = 100;
            job.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(context.CancellationToken);
            await tx.CommitAsync(context.CancellationToken);

            await _notifier.PublishCompletedAsync("simulation", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Simulation job {JobId} failed", jobId);
            await MarkJobFailedAsync(jobId, ex);
            await _notifier.PublishFaultedAsync("simulation", jobId, "Simulation failed. Please retry or contact support.");
        }
    }

    /// <summary>Пометка Failed через отдельный контекст (см. PredictionConsumer.MarkJobFailedAsync).</summary>
    private async Task MarkJobFailedAsync(Guid jobId, Exception ex)
    {
        try
        {
            await using var errorDb = await _dbFactory.CreateDbContextAsync();
            await errorDb.SimulationJobs
                .Where(j => j.Id == jobId && j.Status != JobStatus.Completed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, JobStatus.Failed)
                    .SetProperty(j => j.Error, ex.Message.Length > 4000 ? ex.Message[..4000] : ex.Message)
                    .SetProperty(j => j.CompletedAtUtc, DateTime.UtcNow));
        }
        catch (Exception persistEx)
        {
            _logger.LogCritical(persistEx, "Failed to mark simulation job {JobId} as Failed", jobId);
            throw;
        }
    }
}
