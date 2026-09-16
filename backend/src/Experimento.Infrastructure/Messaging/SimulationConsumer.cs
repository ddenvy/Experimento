using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Experimento.Infrastructure.Data;
using Experimento.Infrastructure.Simulations;
using System.Text.Json;

namespace Experimento.Infrastructure.Messaging;

/// <summary>
/// Consumes simulation jobs: runs the stress-test engine and persists ranked candidates.
/// </summary>
public class SimulationConsumer : IConsumer<SubmitSimulationCommand>
{
    private readonly AppDbContext _db;
    private readonly SimulationEngine _engine;
    private readonly IJobNotifier _notifier;
    private readonly ILogger<SimulationConsumer> _logger;

    public SimulationConsumer(AppDbContext db, SimulationEngine engine, IJobNotifier notifier,
        ILogger<SimulationConsumer> logger)
    {
        _db = db;
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
                version.Components.Select(c => new ComponentSnapshot(c.ChemicalName, c.CasNumber, c.Formula, c.MolarMass, c.Proportion, c.Role)).ToList(),
                new ConditionsSnapshot(version.Conditions.TemperatureCelsius, version.Conditions.PressureKPa,
                    version.Conditions.PhTarget, version.Conditions.Solvent, version.Conditions.DeliveryTarget),
                version.Formulation?.TargetPurpose ?? string.Empty);

            var runResult = await _engine.RunAsync(jobId, snapshot, job.ConfigJson, context.CancellationToken);

            var result = new SimulationResult
            {
                JobId = jobId,
                Summary = runResult.Summary,
                IterationsExecuted = runResult.IterationsExecuted
            };
            _db.SimulationResults.Add(result);
            await _db.SaveChangesAsync();

            // Persist top 50 candidates
            var top = runResult.RankedCandidates.Take(50).ToList();
            foreach (var c in top)
            {
                _db.SimulationCandidates.Add(new SimulationCandidate
                {
                    ResultId = result.Id,
                    ParametersJson = c.ParametersJson,
                    SuccessProbability = c.SuccessProbability,
                    Score = c.Score,
                    Rank = c.Rank
                });
            }
            await _db.SaveChangesAsync();

            var bestEntity = await _db.SimulationCandidates
                .FirstOrDefaultAsync(c => c.ResultId == result.Id && c.Rank == 1);
            result.BestCandidateId = bestEntity?.Id;
            await _db.SaveChangesAsync();

            job.Status = JobStatus.Completed;
            job.Progress = 100;
            job.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _notifier.PublishCompletedAsync("simulation", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Simulation job {JobId} failed", jobId);
            job.Status = JobStatus.Failed;
            job.Error = ex.Message;
            job.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _notifier.PublishFaultedAsync("simulation", jobId, "Simulation failed. Please retry or contact support.");
        }
    }
}
