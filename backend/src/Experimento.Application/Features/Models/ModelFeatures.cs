using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Experimento.Application.Features.Models;

public record ListModelsQuery : IRequest<IReadOnlyList<ModelRegistrationDto>>;

public class ListModelsHandler : IRequestHandler<ListModelsQuery, IReadOnlyList<ModelRegistrationDto>>
{
    private readonly IAppDbContext _db;
    public ListModelsHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ModelRegistrationDto>> Handle(ListModelsQuery request, CancellationToken ct)
    {
        return await _db.ModelRegistrations
            .OrderBy(m => m.RegisteredAtUtc)
            .Select(m => new ModelRegistrationDto(m.Id, m.Name, m.Version, m.Description, m.ContextOfUse, m.RegisteredAtUtc))
            .ToListAsync(ct);
    }
}

/// <summary>
/// Scorecard зарегистрированных моделей: число прогнозов и лабораторных исходов
/// в скоупе пользователя, средняя абсолютная ошибка и смещение калибровки.
/// </summary>
public record GetModelScorecardsQuery(Guid UserId = default) : IRequest<IReadOnlyList<ModelScorecardDto>>;

public class GetModelScorecardsHandler : IRequestHandler<GetModelScorecardsQuery, IReadOnlyList<ModelScorecardDto>>
{
    private readonly IAppDbContext _db;
    public GetModelScorecardsHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ModelScorecardDto>> Handle(GetModelScorecardsQuery request, CancellationToken ct)
    {
        // Скоуп считается так же, как в GetCalibrationStatsHandler:
        // свои job'ы либо job'ы в проектах, созданных пользователем.
        var scope = _db.PredictionResults.AsQueryable();
        if (request.UserId != Guid.Empty)
        {
            scope = scope.Where(r =>
                r.Job.RequestedBy == request.UserId ||
                r.Job.Version.Formulation.Project.CreatedBy == request.UserId);
        }

        var rows = await scope
            .Select(r => new
            {
                r.ModelRegistrationId,
                r.SuccessProbability,
                OutcomeSuccess = r.Outcome != null ? (bool?)r.Outcome.ActualSuccess : null
            })
            .ToListAsync(ct);

        var models = await _db.ModelRegistrations
            .OrderBy(m => m.RegisteredAtUtc)
            .Select(m => new { m.Id, m.Name, m.Version, m.ContextOfUse })
            .ToListAsync(ct);

        return models.Select(m =>
        {
            var group = rows.Where(r => r.ModelRegistrationId == m.Id).ToList();
            var withOutcome = group.Where(r => r.OutcomeSuccess.HasValue).ToList();
            var errors = withOutcome
                .Select(r => Math.Abs(r.SuccessProbability - (r.OutcomeSuccess!.Value ? 1.0 : 0.0)))
                .ToList();
            var biases = withOutcome
                .Select(r => r.SuccessProbability - (r.OutcomeSuccess!.Value ? 1.0 : 0.0))
                .ToList();

            return new ModelScorecardDto(
                m.Id,
                $"{m.Name} ({m.Version})",
                m.Version,
                m.ContextOfUse,
                group.Count,
                withOutcome.Count,
                errors.Count > 0 ? errors.Average() : 0,
                biases.Count > 0 ? biases.Average() : 0);
        }).ToList();
    }
}
