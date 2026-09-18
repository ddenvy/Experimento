using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Experimento.Application.Features.Formulations;

/// <summary>Точка стабильности из запроса: температура, время выдержки и содержание вещества.</summary>
public record StabilityPointInput(double TemperatureCelsius, double TimeDays, double AssayPercent);

public record StabilityPointDto(Guid Id, double TemperatureCelsius, double TimeDays, double AssayPercent);

public record StabilityStudyDto(
    Guid Id,
    Guid VersionId,
    int VersionNumber,
    string? Notes,
    DateTime CreatedAtUtc,
    IReadOnlyList<StabilityPointDto> Points);

public record CreateStabilityStudyCommand(
    Guid VersionId,
    IReadOnlyList<StabilityPointInput> Points,
    string? Notes,
    Guid CreatedBy) : IRequest<StabilityStudyDto>;

public record ListStabilityStudiesQuery(Guid VersionId, Guid UserId = default)
    : IRequest<IReadOnlyList<StabilityStudyDto>>;

public record GetStabilityAssessmentQuery(Guid StudyId, Guid UserId = default)
    : IRequest<StabilityAssessmentDto>;

public record DeleteStabilityStudyCommand(Guid StudyId, Guid UserId = default) : IRequest;

public class CreateStabilityStudyValidator : AbstractValidator<CreateStabilityStudyCommand>
{
    /// <summary>Разумный предел на одно исследование: у реальных исследований точки исчисляются десятками.</summary>
    private const int MaxPoints = 100;

    public CreateStabilityStudyValidator()
    {
        RuleFor(x => x.Points).NotEmpty().WithMessage("A stability study needs at least one measurement point.");
        RuleFor(x => x.Points).Must(p => p.Count <= MaxPoints)
            .WithMessage($"A stability study cannot hold more than {MaxPoints} points.");

        RuleForEach(x => x.Points).ChildRules(point =>
        {
            point.RuleFor(p => p.TemperatureCelsius).InclusiveBetween(-80, 200)
                .WithMessage("Storage temperature must be between -80 and 200 °C.");
            point.RuleFor(p => p.TimeDays).GreaterThanOrEqualTo(0)
                .WithMessage("Measurement time must not be negative.");
            point.RuleFor(p => p.AssayPercent).GreaterThan(0).LessThanOrEqualTo(100)
                .WithMessage("Assay must be in the (0, 100] range as a percentage of the initial content.");
        });
    }
}

public class CreateStabilityStudyHandler : IRequestHandler<CreateStabilityStudyCommand, StabilityStudyDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;

    public CreateStabilityStudyHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<StabilityStudyDto> Handle(CreateStabilityStudyCommand request, CancellationToken ct)
    {
        var version = await _db.FormulationVersions
            .FirstOrDefaultAsync(v => v.Id == request.VersionId, ct)
            ?? throw new NotFoundException($"Version {request.VersionId} not found.");

        if (!await _auth.OwnsVersionAsync(request.VersionId, request.CreatedBy, ct))
            throw new ForbiddenException();

        var study = new StabilityStudy
        {
            VersionId = version.Id,
            Notes = request.Notes,
            CreatedBy = request.CreatedBy,
            Points = request.Points.Select(p => new StabilityPoint
            {
                TemperatureCelsius = p.TemperatureCelsius,
                TimeDays = p.TimeDays,
                AssayPercent = p.AssayPercent
            }).ToList()
        };
        study.EnsureValid();

        _db.StabilityStudies.Add(study);
        await _db.SaveChangesAsync(ct);

        return new StabilityStudyDto(
            study.Id,
            study.VersionId,
            version.VersionNumber,
            study.Notes,
            study.CreatedAtUtc,
            study.Points
                .OrderBy(p => p.TemperatureCelsius)
                .ThenBy(p => p.TimeDays)
                .Select(p => new StabilityPointDto(p.Id, p.TemperatureCelsius, p.TimeDays, p.AssayPercent))
                .ToList());
    }
}

public class ListStabilityStudiesHandler : IRequestHandler<ListStabilityStudiesQuery, IReadOnlyList<StabilityStudyDto>>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;

    public ListStabilityStudiesHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<IReadOnlyList<StabilityStudyDto>> Handle(ListStabilityStudiesQuery request, CancellationToken ct)
    {
        if (!await _auth.OwnsVersionAsync(request.VersionId, request.UserId, ct))
            throw new ForbiddenException();

        var studies = await _db.StabilityStudies
            .Where(s => s.VersionId == request.VersionId)
            .Include(s => s.Points)
            .Include(s => s.Version)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(ct);

        return studies.Select(s => new StabilityStudyDto(
            s.Id,
            s.VersionId,
            s.Version.VersionNumber,
            s.Notes,
            s.CreatedAtUtc,
            s.Points
                .OrderBy(p => p.TemperatureCelsius)
                .ThenBy(p => p.TimeDays)
                .Select(p => new StabilityPointDto(p.Id, p.TemperatureCelsius, p.TimeDays, p.AssayPercent))
                .ToList())).ToList();
    }
}

public class GetStabilityAssessmentHandler : IRequestHandler<GetStabilityAssessmentQuery, StabilityAssessmentDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;

    public GetStabilityAssessmentHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<StabilityAssessmentDto> Handle(GetStabilityAssessmentQuery request, CancellationToken ct)
    {
        var study = await _db.StabilityStudies
            .Include(s => s.Points)
            .FirstOrDefaultAsync(s => s.Id == request.StudyId, ct)
            ?? throw new NotFoundException($"Stability study {request.StudyId} not found.");

        if (!await _auth.OwnsStabilityStudyAsync(request.StudyId, request.UserId, ct))
            throw new ForbiddenException();

        var measurements = study.Points
            .Select(p => new StabilityMeasurement(p.TemperatureCelsius, p.TimeDays, p.AssayPercent))
            .ToList();

        return StabilityKinetics.Assess(study.VersionId, measurements);
    }
}

public class DeleteStabilityStudyHandler : IRequestHandler<DeleteStabilityStudyCommand>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;

    public DeleteStabilityStudyHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task Handle(DeleteStabilityStudyCommand request, CancellationToken ct)
    {
        var study = await _db.StabilityStudies
            .FirstOrDefaultAsync(s => s.Id == request.StudyId, ct)
            ?? throw new NotFoundException($"Stability study {request.StudyId} not found.");

        if (!await _auth.OwnsStabilityStudyAsync(request.StudyId, request.UserId, ct))
            throw new ForbiddenException();

        // Точки — часть исследования и удаляются вместе с ним.
        _db.StabilityStudies.Remove(study);
        await _db.SaveChangesAsync(ct);
    }
}
