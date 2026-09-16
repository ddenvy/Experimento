using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Experimento.Application.Features.Predictions;

public record SubmitPredictionCommand(Guid VersionId, Guid RequestedBy) : IRequest<PredictionJobDto>;
public record GetPredictionJobQuery(Guid JobId, Guid UserId = default) : IRequest<PredictionJobDto>;
public record GetPredictionResultQuery(Guid JobId, Guid UserId = default) : IRequest<PredictionResultDto>;
public record SubmitReviewCommand(Guid ResultId, Guid ReviewerUserId, string Decision, string? Comment) : IRequest<ReviewDto>;
public record RecordOutcomeCommand(Guid ResultId, bool ActualSuccess, string ActualMetricsJson, string? Notes, Guid RecordedBy) : IRequest<OutcomeDto>;
public record GetCalibrationStatsQuery(Guid UserId = default) : IRequest<CalibrationStatsDto>;

public class SubmitPredictionHandler : IRequestHandler<SubmitPredictionCommand, PredictionJobDto>
{
    private readonly IAppDbContext _db;
    private readonly IPublishEndpoint _publish;
    private readonly ResourceAuthorization _auth;
    public SubmitPredictionHandler(IAppDbContext db, IPublishEndpoint publish, ResourceAuthorization auth)
        => (_db, _publish, _auth) = (db, publish, auth);

    public async Task<PredictionJobDto> Handle(SubmitPredictionCommand request, CancellationToken ct)
    {
        if (!await _auth.OwnsVersionAsync(request.VersionId, request.RequestedBy, ct))
            throw new ForbiddenException();

        var job = new PredictionJob
        {
            VersionId = request.VersionId,
            RequestedBy = request.RequestedBy,
            Status = JobStatus.Pending
        };
        _db.PredictionJobs.Add(job);
        await _db.SaveChangesAsync(ct);
        await _publish.Publish(new Messaging.SubmitPredictionCommand(job.Id), ct);
        return new PredictionJobDto(job.Id, job.VersionId, job.Status.ToString(), job.Progress, job.Stage, job.CreatedAtUtc);
    }
}

public class GetPredictionJobHandler : IRequestHandler<GetPredictionJobQuery, PredictionJobDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public GetPredictionJobHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<PredictionJobDto> Handle(GetPredictionJobQuery request, CancellationToken ct)
    {
        var job = await _db.PredictionJobs.FindAsync([request.JobId], ct)
                  ?? throw new NotFoundException($"Job {request.JobId} not found.");
        if (!await _auth.OwnsPredictionJobAsync(request.JobId, request.UserId, ct))
            throw new ForbiddenException();

        return new PredictionJobDto(job.Id, job.VersionId, job.Status.ToString(), job.Progress, job.Stage, job.CreatedAtUtc);
    }
}

public class GetPredictionResultHandler : IRequestHandler<GetPredictionResultQuery, PredictionResultDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public GetPredictionResultHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<PredictionResultDto> Handle(GetPredictionResultQuery request, CancellationToken ct)
    {
        var result = await _db.PredictionResults
            .Include(r => r.ModelRegistration)
            .Include(r => r.RationaleItems)
            .FirstOrDefaultAsync(r => r.JobId == request.JobId, ct)
            ?? throw new NotFoundException($"Result for job {request.JobId} not found.");
        if (!await _auth.OwnsPredictionJobAsync(request.JobId, request.UserId, ct))
            throw new ForbiddenException();

        var items = result.RationaleItems.Select(i =>
        {
            var sources = JsonSerializer.Deserialize<List<RationaleSourceDto>>(i.SourcesJson) ?? new List<RationaleSourceDto>();
            return new RationaleItemDto(i.Id, i.Category.ToString(), i.Claim, i.Explanation, i.Confidence, sources);
        }).ToList();

        return new PredictionResultDto(result.Id, result.JobId, result.ModelRegistrationId,
            result.ModelRegistration.DisplayName, result.SuccessProbability, result.ToxicityScore,
            result.StabilityScore, result.SideRiskLevel.ToString(), result.Summary, items);
    }
}

public class SubmitReviewHandler : IRequestHandler<SubmitReviewCommand, ReviewDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public SubmitReviewHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<ReviewDto> Handle(SubmitReviewCommand request, CancellationToken ct)
    {
        if (!Enum.TryParse<ReviewDecision>(request.Decision, true, out var decision))
            throw new BadRequestException($"Invalid review decision: {request.Decision}");
        if (!await _auth.OwnsPredictionResultAsync(request.ResultId, request.ReviewerUserId, ct))
            throw new ForbiddenException();

        var review = new PredictionReview
        {
            ResultId = request.ResultId,
            ReviewerUserId = request.ReviewerUserId,
            Decision = decision,
            Comment = request.Comment
        };
        _db.PredictionReviews.Add(review);
        await _db.SaveChangesAsync(ct);
        return new ReviewDto(review.Id, review.Decision.ToString(), review.Comment, review.CreatedAtUtc);
    }
}

public class RecordOutcomeHandler : IRequestHandler<RecordOutcomeCommand, OutcomeDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public RecordOutcomeHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<OutcomeDto> Handle(RecordOutcomeCommand request, CancellationToken ct)
    {
        if (!await _auth.OwnsPredictionResultAsync(request.ResultId, request.RecordedBy, ct))
            throw new ForbiddenException();

        var outcome = new ExperimentOutcome
        {
            ResultId = request.ResultId,
            ActualSuccess = request.ActualSuccess,
            ActualMetricsJson = request.ActualMetricsJson,
            Notes = request.Notes,
            RecordedBy = request.RecordedBy
        };
        _db.ExperimentOutcomes.Add(outcome);
        await _db.SaveChangesAsync(ct);
        return new OutcomeDto(outcome.Id, outcome.ActualSuccess, outcome.ActualMetricsJson, outcome.Notes, outcome.RecordedAtUtc);
    }
}

public class GetCalibrationStatsHandler : IRequestHandler<GetCalibrationStatsQuery, CalibrationStatsDto>
{
    private readonly IAppDbContext _db;
    public GetCalibrationStatsHandler(IAppDbContext db) => _db = db;

    public async Task<CalibrationStatsDto> Handle(GetCalibrationStatsQuery request, CancellationToken ct)
    {
        // Метрики калибровки считаются только по результатам, доступным текущему пользователю.
        var scope = _db.PredictionResults.AsQueryable();
        if (request.UserId != Guid.Empty)
        {
            scope = scope.Where(r =>
                r.Job.RequestedBy == request.UserId ||
                r.Job.Version.Formulation.Project.CreatedBy == request.UserId);
        }

        var total = await scope.CountAsync(ct);
        var withOutcome = await scope.Where(r => r.Outcome != null).ToListAsync(ct);

        var errors = withOutcome.Select(r => Math.Abs(r.SuccessProbability - (r.Outcome!.ActualSuccess ? 1.0 : 0.0))).ToList();
        var biases = withOutcome.Select(r => r.SuccessProbability - (r.Outcome!.ActualSuccess ? 1.0 : 0.0)).ToList();

        return new CalibrationStatsDto(
            total,
            withOutcome.Count,
            errors.Count > 0 ? errors.Average() : 0,
            biases.Count > 0 ? biases.Average() : 0);
    }
}
