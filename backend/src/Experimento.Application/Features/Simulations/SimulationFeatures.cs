using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Experimento.Application.Features.Simulations;

/// <summary>Параметры стресс-теста, приходящие от клиента.</summary>
public record SubmitSimulationRequest(
    int Iterations = 100,
    bool VaryConcentrations = true,
    bool VaryTemperature = true,
    bool VaryPh = false,
    int Seed = 42,
    string TargetMetric = "success");

public record SubmitSimulationCommand(
    Guid VersionId,
    int Iterations,
    bool VaryConcentrations,
    bool VaryTemperature,
    bool VaryPh,
    int Seed,
    string TargetMetric,
    Guid RequestedBy) : IRequest<SimulationJobDto>;

public record GetSimulationJobQuery(Guid JobId, Guid UserId = default) : IRequest<SimulationJobDto>;
public record GetSimulationResultQuery(Guid JobId, Guid UserId = default) : IRequest<SimulationResultDto>;
public record ListSimulationRunsQuery(Guid VersionId, Guid UserId = default)
    : IRequest<IReadOnlyList<SimulationRunSummaryDto>>;

public class SubmitSimulationValidator : AbstractValidator<SubmitSimulationCommand>
{
    private static readonly string[] AllowedMetrics = ["success", "stability", "toxicity"];

    public SubmitSimulationValidator()
    {
        // Жёсткий потолок нагрузки: не более 1000 итераций predictor'а на один job.
        RuleFor(x => x.Iterations).InclusiveBetween(1, 1000);
        RuleFor(x => x.Seed).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TargetMetric)
            .Must(m => AllowedMetrics.Contains(m, StringComparer.OrdinalIgnoreCase))
            .WithMessage("TargetMetric must be one of: success, stability, toxicity.");
    }
}

public class SubmitSimulationHandler : IRequestHandler<SubmitSimulationCommand, SimulationJobDto>
{
    private readonly IAppDbContext _db;
    private readonly IPublishEndpoint _publish;
    private readonly ResourceAuthorization _auth;
    public SubmitSimulationHandler(IAppDbContext db, IPublishEndpoint publish, ResourceAuthorization auth)
        => (_db, _publish, _auth) = (db, publish, auth);

    public async Task<SimulationJobDto> Handle(SubmitSimulationCommand request, CancellationToken ct)
    {
        if (!await _auth.OwnsVersionAsync(request.VersionId, request.RequestedBy, ct))
            throw new ForbiddenException();

        // Имена свойств в PascalCase — SimulationConfig в Infrastructure десериализуется опциями по умолчанию.
        var configJson = JsonSerializer.Serialize(new
        {
            Iterations = request.Iterations,
            VaryConcentrations = request.VaryConcentrations,
            VaryTemperature = request.VaryTemperature,
            VaryPh = request.VaryPh,
            Seed = request.Seed,
            TargetMetric = request.TargetMetric
        });

        var job = new SimulationJob
        {
            VersionId = request.VersionId,
            ConfigJson = configJson,
            RequestedBy = request.RequestedBy,
            Status = JobStatus.Pending
        };
        _db.SimulationJobs.Add(job);
        await _db.SaveChangesAsync(ct);
        await _publish.Publish(new Messaging.SubmitSimulationCommand(job.Id), ct);
        return new SimulationJobDto(job.Id, job.VersionId, job.Status.ToString(), job.Progress, job.CreatedAtUtc);
    }
}

public class GetSimulationJobHandler : IRequestHandler<GetSimulationJobQuery, SimulationJobDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public GetSimulationJobHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<SimulationJobDto> Handle(GetSimulationJobQuery request, CancellationToken ct)
    {
        var job = await _db.SimulationJobs.FindAsync([request.JobId], ct)
                  ?? throw new NotFoundException($"Simulation job {request.JobId} not found.");
        if (!await _auth.OwnsSimulationJobAsync(request.JobId, request.UserId, ct))
            throw new ForbiddenException();

        return new SimulationJobDto(job.Id, job.VersionId, job.Status.ToString(), job.Progress, job.CreatedAtUtc);
    }
}

public class GetSimulationResultHandler : IRequestHandler<GetSimulationResultQuery, SimulationResultDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public GetSimulationResultHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<SimulationResultDto> Handle(GetSimulationResultQuery request, CancellationToken ct)
    {
        var result = await _db.SimulationResults
            .Include(r => r.Candidates)
            .FirstOrDefaultAsync(r => r.JobId == request.JobId, ct)
            ?? throw new NotFoundException($"Simulation result for job {request.JobId} not found.");
        if (!await _auth.OwnsSimulationJobAsync(request.JobId, request.UserId, ct))
            throw new ForbiddenException();

        var top = result.Candidates.OrderBy(c => c.Rank).Take(10)
            .Select(c => new SimulationCandidateDto(c.Id, c.Rank, c.SuccessProbability, c.Score, c.ParametersJson)).ToList();
        var best = result.BestCandidateId.HasValue
            ? top.FirstOrDefault(c => c.Id == result.BestCandidateId.Value)
            : top.FirstOrDefault();

        return new SimulationResultDto(result.Id, result.JobId, result.IterationsExecuted, result.Summary, best, top);
    }
}

/// <summary>
/// История симуляций по версии формуляции: последние 20 job'ов со сводкой лучшего кандидата.
/// </summary>
public class ListSimulationRunsHandler : IRequestHandler<ListSimulationRunsQuery, IReadOnlyList<SimulationRunSummaryDto>>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public ListSimulationRunsHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<IReadOnlyList<SimulationRunSummaryDto>> Handle(ListSimulationRunsQuery request, CancellationToken ct)
    {
        if (!await _auth.OwnsVersionAsync(request.VersionId, request.UserId, ct))
            throw new ForbiddenException();

        return await _db.SimulationJobs
            .AsNoTracking()
            .Where(j => j.VersionId == request.VersionId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .Take(20)
            .Select(j => new SimulationRunSummaryDto(
                j.Id,
                j.Result != null ? j.Result.Id : (Guid?)null,
                j.Status.ToString(),
                j.Result != null ? j.Result.IterationsExecuted : 0,
                j.Result != null && j.Result.BestCandidate != null ? j.Result.BestCandidate.SuccessProbability : (double?)null,
                j.Result != null && j.Result.BestCandidate != null ? j.Result.BestCandidate.Score : (double?)null,
                j.CreatedAtUtc))
            .ToListAsync(ct);
    }
}
