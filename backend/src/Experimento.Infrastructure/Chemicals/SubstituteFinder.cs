using Experimento.Application.Abstractions;
using Experimento.Domain.Entities;
using Experimento.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Experimento.Infrastructure.Chemicals.SubstituteScoring;

namespace Experimento.Infrastructure.Chemicals;

/// <summary>
/// Подбор замен компонента по эталонным свойствам из каталога PubChem.
/// Правила близости вынесены в <see cref="SubstituteScoring"/>; здесь — выборка
/// кандидатов, подгрузка их регуляторных статусов и ранжирование.
/// Инструмент поиска альтернатив при перебоях поставок; эквивалентность
/// подтверждается экспериментом, а не этим расчётом.
/// </summary>
public class SubstituteFinder : ISubstituteFinder
{
    // Окно предфильтра по молярной массе: заведомо далёкие по массе вещества не грузим.
    private const double MassWindowLow = 0.5;
    private const double MassWindowHigh = 2.0;

    private readonly IAppDbContext _db;
    public SubstituteFinder(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<SubstituteCandidateDto>?> FindAsync(int pubChemCid, int limit,
        CancellationToken cancellationToken = default)
    {
        var target = await _db.ChemicalCatalog
            .FirstOrDefaultAsync(e => e.PubChemCid == pubChemCid, cancellationToken);
        if (target is null) return null;

        var query = _db.ChemicalCatalog.Where(e => e.PubChemCid != pubChemCid);
        if (target.MolarMass > 0)
        {
            var low = target.MolarMass * MassWindowLow;
            var high = target.MolarMass * MassWindowHigh;
            query = query.Where(e => e.MolarMass >= low && e.MolarMass <= high);
        }

        var candidates = await query.ToListAsync(cancellationToken);
        if (candidates.Count == 0) return Array.Empty<SubstituteCandidateDto>();

        var statuses = await LoadRegulatoryStatusesAsync(candidates, cancellationToken);

        var targetProfile = ChemicalProfile.From(target.Formula, target.Smiles, target.CanonicalName, target.MolarMass);

        return candidates
            .Select(candidate =>
            {
                var status = statuses.GetValueOrDefault(candidate.PubChemCid, RegulationStatus.Compliant);
                var profile = ChemicalProfile.From(
                    candidate.Formula, candidate.Smiles, candidate.CanonicalName, candidate.MolarMass);
                var score = Compute(targetProfile, profile, status);

                return new SubstituteCandidateDto(
                    candidate.PubChemCid,
                    candidate.CanonicalName,
                    candidate.CasNumber,
                    candidate.Formula,
                    candidate.MolarMass,
                    score.Similarity,
                    score.MatchScore,
                    status,
                    score.Signals);
            })
            .OrderByDescending(c => c.MatchScore)
            .ThenBy(c => c.MolarMass)
            .ThenBy(c => c.PubChemCid)
            .Take(limit)
            .ToList();
    }

    /// <summary>Наивысший регуляторный статус по каждому кандидату — одним запросом.</summary>
    private async Task<Dictionary<int, RegulationStatus>> LoadRegulatoryStatusesAsync(
        IReadOnlyList<ChemicalCatalogEntry> candidates, CancellationToken ct)
    {
        var cids = candidates.Select(c => c.PubChemCid).ToList();
        var rows = await _db.ChemicalRegulations
            .Where(r => cids.Contains(r.ChemicalCatalogEntry.PubChemCid))
            .Select(r => new { r.ChemicalCatalogEntry.PubChemCid, r.Status })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.PubChemCid)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Status));
    }
}
