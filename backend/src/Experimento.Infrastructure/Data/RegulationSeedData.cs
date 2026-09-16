using Experimento.Domain.Entities;
using Experimento.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Experimento.Infrastructure.Data;

/// <summary>
/// Инициализация справочника регуляторных статусов веществ.
/// Набор демонстрационный: хорошо известные вещества из списков PFAS / REACH-SVHC / Prop 65 / VOC.
/// В продакшене заменяется на синхронизацию с реальными базами регуляторов.
/// </summary>
public static class RegulationSeedData
{
    private static readonly (int PubChemCid, RegulationAuthority Authority, RegulationStatus Status, string Reason, string? SourceUrl)[] _seeds =
    {
        // PFAS — перфторированные вещества
        (24407, RegulationAuthority.EpaPfas, RegulationStatus.Banned,
            "Perfluorooctanesulfonic acid (PFOS) — запрещён EPA TSCA как PFAS",
            "https://www.epa.gov/assessing-and-managing-chemicals-under-tsca/per-and-polyfluoroalkyl-substances-pfas"),
        (9552, RegulationAuthority.EpaPfas, RegulationStatus.Banned,
            "Perfluorooctanoic acid (PFOA) — запрещён EPA TSCA как PFAS",
            "https://www.epa.gov/assessing-and-managing-chemicals-under-tsca/per-and-polyfluoroalkyl-substances-pfas"),

        // REACH SVHC (Substances of Very High Concern)
        (9552, RegulationAuthority.ReachSvhc, RegulationStatus.Restricted,
            "PFOA — в списке SVHC (репродуктивная токсичность)",
            "https://echa.europa.eu/candidate-list-table"),
        (712, RegulationAuthority.ReachSvhc, RegulationStatus.Restricted,
            "Formaldehyde — в списке SVHC (канцероген категории 1B)",
            "https://echa.europa.eu/candidate-list-table"),
        (241, RegulationAuthority.ReachSvhc, RegulationStatus.Restricted,
            "Benzene — в списке SVHC (канцероген категории 1A)",
            "https://echa.europa.eu/candidate-list-table"),
        (11763, RegulationAuthority.ReachSvhc, RegulationStatus.Restricted,
            "Toluene-2,4-diisocyanate (TDI) — в списке SVHC (респираторный сенсибилизатор)",
            "https://echa.europa.eu/candidate-list-table"),

        // California Proposition 65
        (712, RegulationAuthority.CaliforniaProp65, RegulationStatus.Restricted,
            "Formaldehyde — в списке Proposition 65 (канцероген)",
            "https://oehha.ca.gov/proposition-65"),
        (241, RegulationAuthority.CaliforniaProp65, RegulationStatus.Banned,
            "Benzene — в списке Proposition 65 (канцероген)",
            "https://oehha.ca.gov/proposition-65"),
        (6783, RegulationAuthority.CaliforniaProp65, RegulationStatus.Restricted,
            "Diethyl phthalate (DEP) — в списке Proposition 65 (репродуктивная токсичность)",
            "https://oehha.ca.gov/proposition-65"),

        // VOC — летучие органические соединения
        (11763, RegulationAuthority.Voc, RegulationStatus.Restricted,
            "Toluene diisocyanate — высокий VOC, лимиты эмиссий по EPA",
            "https://www.epa.gov/indoor-air-quality-iaq/volatile-organic-compounds-impact-indoor-air-quality"),
        (180, RegulationAuthority.Voc, RegulationStatus.Compliant,
            "Acetone — низкий VOC, исключения по ряду норм",
            "https://www.epa.gov/indoor-air-quality-iaq/volatile-organic-compounds-impact-indoor-air-quality"),
    };

    /// <summary>
    /// Добавляет записи в ChemicalCatalog (если вещества ещё не закэшированы)
    /// и заполняет ChemicalRegulations базовым набором.
    /// Идемпотентно: для существующих записей ничего не делаем.
    /// </summary>
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        var cids = _seeds.Select(s => s.PubChemCid).Distinct().ToArray();
        var existing = await db.ChemicalCatalog
            .Where(e => cids.Contains(e.PubChemCid))
            .ToDictionaryAsync(e => e.PubChemCid, ct);

        var newRegs = new List<ChemicalRegulation>();
        var now = DateTime.UtcNow;

        foreach (var (cid, authority, status, reason, url) in _seeds)
        {
            if (!existing.TryGetValue(cid, out var entry))
            {
                entry = new ChemicalCatalogEntry
                {
                    PubChemCid = cid,
                    CanonicalName = FallbackName(cid),
                    MolarMass = 0, // заполнится при первом реальном запросе через PubChem
                    CachedAtUtc = now,
                };
                db.ChemicalCatalog.Add(entry);
                existing[cid] = entry;
            }
        }

        await db.SaveChangesAsync(ct);

        // После SaveChanges у всех entry есть Id — строим словарь заново.
        var byCid = await db.ChemicalCatalog
            .Where(e => cids.Contains(e.PubChemCid))
            .ToDictionaryAsync(e => e.PubChemCid, ct);

        var existingRegs = await db.ChemicalRegulations
            .Where(r => cids.Contains(r.ChemicalCatalogEntry.PubChemCid))
            .Select(r => new { r.ChemicalCatalogEntry.PubChemCid, r.Authority })
            .ToListAsync(ct);
        var seen = new HashSet<(int Cid, RegulationAuthority Auth)>(
            existingRegs.Select(r => (r.PubChemCid, r.Authority)));

        foreach (var (cid, authority, status, reason, url) in _seeds)
        {
            if (seen.Contains((cid, authority))) continue;
            var entry = byCid[cid];
            newRegs.Add(new ChemicalRegulation
            {
                ChemicalCatalogEntryId = entry.Id,
                Authority = authority,
                Status = status,
                Reason = reason,
                SourceUrl = url,
                UpdatedAtUtc = now,
            });
        }

        if (newRegs.Count > 0)
        {
            db.ChemicalRegulations.AddRange(newRegs);
            await db.SaveChangesAsync(ct);
        }
    }

    private static string FallbackName(int cid) => cid switch
    {
        24407 => "Perfluorooctanesulfonic acid",
        9552 => "Perfluorooctanoic acid",
        712 => "Formaldehyde",
        241 => "Benzene",
        11763 => "Toluene-2,4-diisocyanate",
        6783 => "Diethyl phthalate",
        180 => "Acetone",
        _ => $"CID {cid}",
    };
}
