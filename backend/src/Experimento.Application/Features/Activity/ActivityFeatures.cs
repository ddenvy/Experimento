using MediatR;
using Microsoft.EntityFrameworkCore;
using Experimento.Application.Abstractions;
using Experimento.Application.DTOs;

namespace Experimento.Application.Features.Activity;

/// <summary>
/// Лента последних прогонов пользователя (предсказания + симуляции)
/// для командного центра. Только job'ы, инициированные этим пользователем.
/// </summary>
public record GetRecentRunsQuery(Guid UserId) : IRequest<IReadOnlyList<RecentRunDto>>;

public class GetRecentRunsHandler : IRequestHandler<GetRecentRunsQuery, IReadOnlyList<RecentRunDto>>
{
    private const int Take = 10;
    private readonly IAppDbContext _db;
    public GetRecentRunsHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<RecentRunDto>> Handle(GetRecentRunsQuery request, CancellationToken ct)
    {
        var predictions = await _db.PredictionJobs
            .AsNoTracking()
            .Where(j => j.RequestedBy == request.UserId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .Take(Take)
            .Select(j => new RecentRunDto(
                "prediction", j.Id, j.Status.ToString(),
                j.Version.Formulation.Project.Id, j.Version.Formulation.Project.Name,
                j.Version.Formulation.Id, j.Version.Formulation.Name, j.Version.VersionNumber,
                j.Result != null ? j.Result.SuccessProbability : (double?)null,
                j.CreatedAtUtc))
            .ToListAsync(ct);

        var simulations = await _db.SimulationJobs
            .AsNoTracking()
            .Where(j => j.RequestedBy == request.UserId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .Take(Take)
            .Select(j => new RecentRunDto(
                "simulation", j.Id, j.Status.ToString(),
                j.Version.Formulation.Project.Id, j.Version.Formulation.Project.Name,
                j.Version.Formulation.Id, j.Version.Formulation.Name, j.Version.VersionNumber,
                j.Result != null && j.Result.BestCandidate != null
                    ? j.Result.BestCandidate.SuccessProbability
                    : (double?)null,
                j.CreatedAtUtc))
            .ToListAsync(ct);

        return predictions.Concat(simulations)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(Take)
            .ToList();
    }
}
