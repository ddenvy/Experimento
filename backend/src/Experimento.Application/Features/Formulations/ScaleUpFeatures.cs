using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Experimento.Application.Features.Formulations;

/// <summary>
/// Оценка готовности версии формуляции к переносу на целевой объём партии.
/// </summary>
public record GetScaleUpAssessmentQuery(Guid VersionId, double TargetVolumeLitres, Guid UserId = default)
    : IRequest<ScaleUpAssessmentDto>;

public class GetScaleUpAssessmentValidator : AbstractValidator<GetScaleUpAssessmentQuery>
{
    public GetScaleUpAssessmentValidator()
    {
        RuleFor(x => x.TargetVolumeLitres).InclusiveBetween(1, 10_000)
            .WithMessage("Target volume must be between 1 and 10000 litres.");
    }
}

public class GetScaleUpAssessmentHandler : IRequestHandler<GetScaleUpAssessmentQuery, ScaleUpAssessmentDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    private readonly IScaleUpAssessment _assessment;

    public GetScaleUpAssessmentHandler(
        IAppDbContext db, ResourceAuthorization auth, IScaleUpAssessment assessment)
        => (_db, _auth, _assessment) = (db, auth, assessment);

    public async Task<ScaleUpAssessmentDto> Handle(GetScaleUpAssessmentQuery request, CancellationToken ct)
    {
        var version = await _db.FormulationVersions
            .Include(v => v.Components)
            .FirstOrDefaultAsync(v => v.Id == request.VersionId, ct)
            ?? throw new NotFoundException($"Version {request.VersionId} not found.");

        if (!await _auth.OwnsVersionAsync(request.VersionId, request.UserId, ct))
            throw new ForbiddenException();

        return _assessment.Assess(version.Id, version.VersionNumber,
            version.Components.ToList(), version.Conditions, request.TargetVolumeLitres);
    }
}
