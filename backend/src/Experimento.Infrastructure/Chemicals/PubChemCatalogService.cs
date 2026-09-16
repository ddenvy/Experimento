using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Experimento.Application.Abstractions;
using Experimento.Application.DTOs;
using Experimento.Domain.Entities;
using Experimento.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Experimento.Infrastructure.Chemicals;

/// <summary>
/// Клиент каталога PubChem (PUG REST + Autocomplete).
/// Пониманием одно поле поиска: название/синоним, CAS-номер и молекулярная формула.
/// Автоподсказки кэшируются в памяти, результаты резолва навсегда сохраняются
/// в таблицу ChemicalCatalog — повторный выбор вещества не требует внешних запросов.
/// </summary>
public class PubChemCatalogService : IChemicalCatalogService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IAppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PubChemCatalogService> _logger;

    private static readonly TimeSpan SuggestCacheTtl = TimeSpan.FromMinutes(30);
    private static readonly Regex CasPattern = new(@"^\d{2,7}-\d{2}-\d$", RegexOptions.Compiled);
    private const string PropertyFields = "Title,MolecularFormula,MolecularWeight,CanonicalSMILES";

    public PubChemCatalogService(IHttpClientFactory httpFactory, IAppDbContext db,
        IMemoryCache cache, ILogger<PubChemCatalogService> logger)
    {
        _httpFactory = httpFactory;
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ChemicalSuggestion>> SuggestAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        query = query.Trim();
        if (query.Length < 2) return Array.Empty<ChemicalSuggestion>();
        limit = Math.Clamp(limit, 1, 20);

        var cacheKey = $"pubchem:suggest:v2:{limit}:{query.ToLowerInvariant()}";
        if (_cache.TryGetValue<List<ChemicalSuggestion>>(cacheKey, out var cached) && cached is not null)
            return cached;

        var kind = ChemicalQueryClassifier.Classify(query);

        // Локальный каталог и внешний источник опрашиваем параллельно.
        var localTask = SearchLocalCatalogAsync(query, limit, cancellationToken);
        var externalTask = kind switch
        {
            ChemicalQueryKind.Cas => SuggestByCasAsync(query, cancellationToken),
            ChemicalQueryKind.Formula => SuggestByFormulaAsync(query, limit, cancellationToken),
            _ => SuggestByAutocompleteAsync(query, limit, cancellationToken)
        };
        await Task.WhenAll(localTask, externalTask);

        // Слияние с дедупликацией: сначала локальные записи (мгновенные, с CID),
        // затем внешние кандидаты с CID (формула/CAS), затем варианты названий без CID.
        var result = new List<ChemicalSuggestion>();
        var seenCids = new HashSet<int>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRange(IEnumerable<ChemicalSuggestion> items)
        {
            foreach (var s in items)
            {
                if (s.PubChemCid is { } cid && !seenCids.Add(cid)) continue;
                if (!seenNames.Add(s.Name)) continue;
                if (result.Count < limit) result.Add(s);
            }
        }

        AddRange(localTask.Result);
        AddRange(externalTask.Result);

        _cache.Set(cacheKey, result, SuggestCacheTtl);
        return result;
    }

    public async Task<ChemicalDto?> ResolveByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (name.Length < 2) return null;

        // 1. Точное попадание в локальном каталоге (по каноническому имени, регистр игнорируем).
        var cachedByName = await _db.ChemicalCatalog
            .FirstOrDefaultAsync(e => e.CanonicalName.ToLower() == name.ToLower(), cancellationToken);
        if (cachedByName is not null)
        {
            // Запись могла быть создана до появления структурных полей — дозаполним по CID.
            return cachedByName.Smiles is not null && cachedByName.Formula is not null
                ? ToDto(cachedByName)
                : await ResolveByCidAsync(cachedByName.PubChemCid, cancellationToken);
        }

        // 2. Запрос свойств в PubChem по названию (namespace name понимает синонимы и CAS).
        var propsUrl = $"/rest/pug/compound/name/{Uri.EscapeDataString(name)}/property/{PropertyFields}/JSON";
        using var doc = await GetJsonAsync(propsUrl, cancellationToken);
        if (!TryGetFirstProperty(doc, out var props)) return null;

        return await UpsertFromPropertiesAsync(props, cancellationToken);
    }

    public async Task<ChemicalDto?> ResolveByCidAsync(int cid, CancellationToken cancellationToken = default)
    {
        if (cid <= 0) return null;

        var existing = await _db.ChemicalCatalog
            .FirstOrDefaultAsync(e => e.PubChemCid == cid, cancellationToken);
        if (existing is not null && existing.Smiles is not null && existing.Formula is not null && existing.MolarMass > 0)
            return ToDto(existing);

        var url = $"/rest/pug/compound/cid/{cid}/property/{PropertyFields}/JSON";
        using var doc = await GetJsonAsync(url, cancellationToken);
        if (!TryGetFirstProperty(doc, out var props))
            return existing is not null ? ToDto(existing) : null;

        return await UpsertFromPropertiesAsync(props, cancellationToken);
    }

    public async Task<IReadOnlyList<ChemicalDto>> GetByCidsAsync(IReadOnlyList<int> cids, CancellationToken cancellationToken = default)
    {
        if (cids.Count == 0) return Array.Empty<ChemicalDto>();
        return await _db.ChemicalCatalog
            .Where(e => cids.Contains(e.PubChemCid))
            .Select(e => new ChemicalDto(e.PubChemCid, e.CanonicalName, e.CasNumber, e.Formula, e.MolarMass, e.Smiles))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ChemicalRegulationSummaryDto> GetRegulationsAsync(int cid, CancellationToken cancellationToken = default)
    {
        var regulations = await _db.ChemicalRegulations
            .Where(r => r.ChemicalCatalogEntry.PubChemCid == cid)
            .OrderByDescending(r => r.Status)
            .Select(r => new ChemicalRegulationDto(r.Authority, r.Status, r.Reason, r.SourceUrl))
            .ToListAsync(cancellationToken);

        var highest = regulations.Count == 0
            ? RegulationStatus.Compliant
            : regulations.Max(r => r.Status);

        return new ChemicalRegulationSummaryDto(cid, highest, regulations);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChemicalRegulationSummaryDto>> GetRegulationsBatchAsync(
        IReadOnlyList<int> cids, CancellationToken cancellationToken = default)
    {
        if (cids.Count == 0) return Array.Empty<ChemicalRegulationSummaryDto>();

        var grouped = await _db.ChemicalRegulations
            .Where(r => cids.Contains(r.ChemicalCatalogEntry.PubChemCid))
            .Select(r => new
            {
                r.ChemicalCatalogEntry.PubChemCid,
                r.Authority,
                r.Status,
                r.Reason,
                r.SourceUrl,
            })
            .ToListAsync(cancellationToken);

        var byCid = grouped.GroupBy(r => r.PubChemCid).ToDictionary(
            g => g.Key,
            g => g.Select(r => new ChemicalRegulationDto(r.Authority, r.Status, r.Reason, r.SourceUrl)).ToList()
                as IReadOnlyList<ChemicalRegulationDto>);

        return cids.Select(cid =>
        {
            var regs = byCid.TryGetValue(cid, out var list) ? list : Array.Empty<ChemicalRegulationDto>();
            var highest = regs.Count == 0 ? RegulationStatus.Compliant : regs.Max(r => r.Status);
            return new ChemicalRegulationSummaryDto(cid, highest, regs);
        }).ToList();
    }

    // --- Источники автоподсказок -------------------------------------------------

    /// <summary>Поиск в уже закэшированном локальном каталоге: по названию и CAS.</summary>
    private async Task<List<ChemicalSuggestion>> SearchLocalCatalogAsync(string query, int limit, CancellationToken ct)
    {
        try
        {
            var pattern = "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            var q = query.ToLowerInvariant();
            return await _db.ChemicalCatalog
                .Where(e => EF.Functions.ILike(e.CanonicalName, pattern)
                            || (e.CasNumber != null && EF.Functions.ILike(e.CasNumber, pattern)))
                .OrderByDescending(e => e.CanonicalName.ToLower().StartsWith(q))
                .ThenBy(e => e.CanonicalName)
                .Take(limit)
                .Select(e => new ChemicalSuggestion(e.PubChemCid, e.CanonicalName, e.Formula, "local"))
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local chemical catalog search failed for '{Query}'", query);
            return new List<ChemicalSuggestion>();
        }
    }

    /// <summary>Автодополнение названий через PubChem Autocomplete (CID на этом шаге неизвестен).</summary>
    private async Task<List<ChemicalSuggestion>> SuggestByAutocompleteAsync(string query, int limit, CancellationToken ct)
    {
        try
        {
            var url = $"/rest/autocomplete/compound/{Uri.EscapeDataString(query)}/json?limit={limit}";
            using var doc = await GetJsonAsync(url, ct);
            if (doc is null ||
                !doc.RootElement.TryGetProperty("dictionary_terms", out var terms) ||
                !terms.TryGetProperty("compound", out var compounds))
            {
                return new List<ChemicalSuggestion>();
            }

            return compounds.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => new ChemicalSuggestion(null, s!, null, "name"))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PubChem autocomplete failed for '{Query}'", query);
            return new List<ChemicalSuggestion>();
        }
    }

    /// <summary>CAS-номер резолвится напрямую — это всегда один конкретный кандидат.</summary>
    private async Task<List<ChemicalSuggestion>> SuggestByCasAsync(string cas, CancellationToken ct)
    {
        try
        {
            var url = $"/rest/pug/compound/name/{Uri.EscapeDataString(cas)}/property/Title,MolecularFormula/JSON";
            using var doc = await GetJsonAsync(url, ct);
            if (!TryGetFirstProperty(doc, out var p)) return new List<ChemicalSuggestion>();

            var cid = p.GetProperty("CID").GetInt32();
            var title = p.TryGetProperty("Title", out var t) ? t.GetString() ?? cas : cas;
            var formula = p.TryGetProperty("MolecularFormula", out var f) ? f.GetString() : null;
            return new List<ChemicalSuggestion> { new(cid, title, formula, "cas") };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PubChem CAS lookup failed for '{Cas}'", cas);
            return new List<ChemicalSuggestion>();
        }
    }

    /// <summary>
    /// Поиск по молекулярной формуле в PubChem асинхронный: первый запрос возвращает
    /// ListKey, результаты забираются опросом. У формулы может быть много изомеров,
    /// поэтому отдаём список, а не единственное вещество.
    /// </summary>
    private async Task<List<ChemicalSuggestion>> SuggestByFormulaAsync(string formula, int limit, CancellationToken ct)
    {
        var segment = "property/Title,MolecularFormula/JSON";
        var escaped = Uri.EscapeDataString(formula);
        try
        {
            using var first = await GetJsonAsync(
                $"/rest/pug/compound/formula/{escaped}/{segment}?MaxRecords={limit}", ct);

            if (TryGetPropertyArray(first, out var ready))
                return MapFormulaSuggestions(ready);

            var listKey = first is not null
                          && first.RootElement.TryGetProperty("Waiting", out var waiting)
                          && waiting.TryGetProperty("ListKey", out var keyElement)
                ? keyElement.GetString()
                : null;
            if (listKey is null) return new List<ChemicalSuggestion>();

            // Опрос готовности результата (обычно 1-2 секунды).
            for (var attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(700), ct);
                using var poll = await GetJsonAsync(
                    $"/rest/pug/compound/listkey/{listKey}/{segment}?MaxRecords={limit}", ct);
                if (TryGetPropertyArray(poll, out var properties))
                    return MapFormulaSuggestions(properties);
                if (poll is not null && poll.RootElement.TryGetProperty("Fault", out _))
                    break;
                // Иначе снова Waiting — повторяем опрос.
            }
            return new List<ChemicalSuggestion>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PubChem formula search failed for '{Formula}'", formula);
            return new List<ChemicalSuggestion>();
        }
    }

    private static List<ChemicalSuggestion> MapFormulaSuggestions(JsonElement properties)
    {
        var result = new List<ChemicalSuggestion>();
        foreach (var p in properties.EnumerateArray())
        {
            var cid = p.GetProperty("CID").GetInt32();
            var title = p.TryGetProperty("Title", out var t) ? t.GetString() ?? $"CID {cid}" : $"CID {cid}";
            var formula = p.TryGetProperty("MolecularFormula", out var f) ? f.GetString() : null;
            result.Add(new ChemicalSuggestion(cid, title, formula, "formula"));
        }
        return result;
    }

    // --- Кэширование полной записи ----------------------------------------------

    /// <summary>
    /// Создаёт или дозаполняет запись каталога по свойствам, полученным из PubChem.
    /// CAS и SMILES подтягиваются только при первом полном резолве (доп. запросы).
    /// </summary>
    private async Task<ChemicalDto> UpsertFromPropertiesAsync(JsonElement p, CancellationToken ct)
    {
        var cid = p.GetProperty("CID").GetInt32();
        var title = p.TryGetProperty("Title", out var t) ? t.GetString() ?? $"CID {cid}" : $"CID {cid}";
        var formula = p.TryGetProperty("MolecularFormula", out var f) ? f.GetString() : null;
        var smiles = ReadSmiles(p);
        var molarMass = p.TryGetProperty("MolecularWeight", out var mw) && double.TryParse(mw.GetString(),
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

        var existing = await _db.ChemicalCatalog.FirstOrDefaultAsync(e => e.PubChemCid == cid, ct);
        if (existing is not null)
        {
            var changed = false;
            if (existing.Smiles is null && smiles is not null) { existing.Smiles = smiles; changed = true; }
            if (existing.Formula is null && formula is not null) { existing.Formula = formula; changed = true; }
            if (existing.MolarMass == 0 && molarMass > 0) { existing.MolarMass = molarMass; changed = true; }
            if (existing.CasNumber is null)
            {
                try { existing.CasNumber = await FetchPrimaryCasAsync(cid, ct); changed = true; }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to fetch synonyms/CAS for CID {Cid}", cid); }
            }
            if (changed) await _db.SaveChangesAsync(ct);
            return ToDto(existing);
        }

        string? cas = null;
        try
        {
            cas = await FetchPrimaryCasAsync(cid, ct);
        }
        catch (Exception ex)
        {
            // Отсутствие CAS не должно ломать резолв — это справочная информация.
            _logger.LogWarning(ex, "Failed to fetch synonyms/CAS for CID {Cid}", cid);
        }

        var entry = new ChemicalCatalogEntry
        {
            PubChemCid = cid,
            CanonicalName = title,
            CasNumber = cas,
            Formula = formula,
            Smiles = smiles,
            MolarMass = molarMass
        };
        _db.ChemicalCatalog.Add(entry);
        await _db.SaveChangesAsync(ct);

        return ToDto(entry);
    }

    private async Task<string?> FetchPrimaryCasAsync(int cid, CancellationToken ct)
    {
        var url = $"/rest/pug/compound/cid/{cid}/synonyms/JSON";
        using var doc = await GetJsonAsync(url, ct);
        if (doc is null ||
            !doc.RootElement.TryGetProperty("InformationList", out var list) ||
            !list.TryGetProperty("Information", out var info) ||
            info.GetArrayLength() == 0 ||
            !info[0].TryGetProperty("Synonym", out var synonyms))
        {
            return null;
        }

        // Первый синоним, соответствующий формату CAS, — основной регистрационный номер.
        foreach (var synonym in synonyms.EnumerateArray())
        {
            var value = synonym.GetString();
            if (value is not null && CasPattern.IsMatch(value))
                return value;
        }
        return null;
    }

    // --- HTTP и разбор JSON ------------------------------------------------------

    /// <summary>
    /// PubChem мигрировал с имени CanonicalSMILES на ConnectivitySMILES — принимаем
    /// все известные варианты, чтобы структурные поля не оставались пустыми.
    /// </summary>
    private static string? ReadSmiles(JsonElement p)
    {
        if (p.TryGetProperty("ConnectivitySMILES", out var connectivity)) return connectivity.GetString();
        if (p.TryGetProperty("CanonicalSMILES", out var canonical)) return canonical.GetString();
        return p.TryGetProperty("SMILES", out var generic) ? generic.GetString() : null;
    }

    private static bool TryGetPropertyArray(JsonDocument? doc, out JsonElement properties)
    {
        properties = default;
        return doc is not null
               && doc.RootElement.TryGetProperty("PropertyTable", out var table)
               && table.TryGetProperty("Properties", out properties)
               && properties.ValueKind == JsonValueKind.Array
               && properties.GetArrayLength() > 0;
    }

    private static bool TryGetFirstProperty(JsonDocument? doc, out JsonElement property)
    {
        property = default;
        if (!TryGetPropertyArray(doc, out var array)) return false;
        property = array[0];
        return true;
    }

    private async Task<JsonDocument?> GetJsonAsync(string relativeUrl, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("pubchem");
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var response = await http.GetAsync(relativeUrl, ct);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
            // 404 (PUGREST.NotFound / неоднозначное имя) — валидный пустой результат, не ретраим.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests
                            || (int)response.StatusCode >= 500;
            if (!retryable || attempt == maxAttempts)
                throw new HttpRequestException($"PubChem request failed: {(int)response.StatusCode} {response.StatusCode}");

            await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt * attempt), ct);
        }
        return null;
    }

    private static ChemicalDto ToDto(ChemicalCatalogEntry e) =>
        new(e.PubChemCid, e.CanonicalName, e.CasNumber, e.Formula, e.MolarMass, e.Smiles);
}
