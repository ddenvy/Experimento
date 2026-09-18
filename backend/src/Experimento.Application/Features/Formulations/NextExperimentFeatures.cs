using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Experimento.Application.Features.Formulations;

/// <summary>
/// План следующих экспериментов по формуляции: непроверенные кандидаты симуляций,
/// воспроизведение удачной версии и напоминания записать лабораторный исход.
/// </summary>
public record GetNextExperimentsQuery(Guid FormulationId, int Limit = 5, Guid UserId = default)
    : IRequest<NextExperimentPlanDto>;

public class GetNextExperimentsValidator : AbstractValidator<GetNextExperimentsQuery>
{
    public GetNextExperimentsValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 20);
    }
}

public class GetNextExperimentsHandler : IRequestHandler<GetNextExperimentsQuery, NextExperimentPlanDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;

    public GetNextExperimentsHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<NextExperimentPlanDto> Handle(GetNextExperimentsQuery request, CancellationToken ct)
    {
        if (!await _auth.OwnsFormulationAsync(request.FormulationId, request.UserId, ct))
            throw new ForbiddenException();

        var versions = await _db.FormulationVersions
            .Where(v => v.FormulationId == request.FormulationId)
            .Include(v => v.Components)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync(ct);

        if (versions.Count == 0)
            return new NextExperimentPlanDto(request.FormulationId, 0, 0, null,
                Array.Empty<NextExperimentDto>());

        var versionIds = versions.Select(v => v.Id).ToList();

        // История прогнозов: у версии их может быть несколько, берём последний,
        // но исход ищем по любому прогону — эксперимент проводится один раз.
        var predictions = await _db.PredictionResults
            .Where(r => versionIds.Contains(r.Job.VersionId))
            .Select(r => new
            {
                r.Job.VersionId,
                r.SuccessProbability,
                r.CreatedAtUtc,
                HasOutcome = r.Outcome != null,
                Succeeded = r.Outcome != null && r.Outcome.ActualSuccess
            })
            .ToListAsync(ct);

        var byVersion = predictions.GroupBy(p => p.VersionId).ToDictionary(g => g.Key, g => g.ToList());

        var plannerVersions = versions.Select(v =>
        {
            var runs = byVersion.GetValueOrDefault(v.Id);
            var latest = runs?.OrderByDescending(r => r.CreatedAtUtc).FirstOrDefault();
            var outcomeRun = runs?.Where(r => r.HasOutcome).OrderByDescending(r => r.CreatedAtUtc).FirstOrDefault();
            var reference = outcomeRun ?? latest;

            return new PlannerVersion(
                v.Id,
                v.VersionNumber,
                v.Components.Select(c => new PlannerComponent(c.ChemicalName, c.Proportion)).ToList(),
                new PlannerConditions(v.Conditions.TemperatureCelsius, v.Conditions.PhTarget, v.Conditions.Solvent),
                outcomeRun is not null,
                outcomeRun?.Succeeded ?? false,
                reference?.SuccessProbability,
                reference?.CreatedAtUtc);
        }).ToList();

        // Лучший кандидат последней завершённой симуляции каждой версии.
        var simulationRows = await _db.SimulationJobs
            .Where(j => versionIds.Contains(j.VersionId) && j.Status == JobStatus.Completed && j.Result != null)
            .Select(j => new
            {
                j.VersionId,
                j.CompletedAtUtc,
                Candidates = j.Result!.Candidates
                    .Select(c => new { c.Score, c.SuccessProbability, c.ParametersJson })
                    .ToList()
            })
            .ToListAsync(ct);

        var leads = simulationRows
            .Where(r => r.Candidates.Count > 0)
            .GroupBy(r => r.VersionId)
            .Select(g => g.OrderByDescending(r => r.CompletedAtUtc).First())
            .Select(row =>
            {
                var best = row.Candidates.OrderByDescending(c => c.Score).First();
                return new PlannerLead(row.VersionId, best.Score, best.SuccessProbability,
                    ParseParameters(best.ParametersJson));
            })
            .ToList();

        return NextExperimentPlanner.Build(request.FormulationId, plannerVersions, leads, request.Limit);
    }

    /// <summary>Числовые значения параметров кандидата; нечисловые записи пропускаются.</summary>
    private static IReadOnlyList<SuggestedParameterDto> ParseParameters(string parametersJson)
    {
        using var document = JsonDocument.Parse(parametersJson);
        return document.RootElement.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.Number)
            .Select(p => new SuggestedParameterDto(p.Name, p.Value.GetDouble()))
            .ToList();
    }
}
