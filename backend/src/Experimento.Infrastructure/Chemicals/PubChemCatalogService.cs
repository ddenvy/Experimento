using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Experimento.Application.Abstractions;
using Experimento.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Experimento.Infrastructure.Chemicals;

/// <summary>
/// Клиент каталога PubChem (PUG REST + Autocomplete).
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

    public PubChemCatalogService(IHttpClientFactory httpFactory, IAppDbContext db,
        IMemoryCache cache, ILogger<PubChemCatalogService> logger)
    {
        _httpFactory = httpFactory;
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> SuggestNamesAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        query = query.Trim();
        if (query.Length < 2) return Array.Empty<string>();

        var cacheKey = $"pubchem:suggest:{limit}:{query.ToLowerInvariant()}";
        if (_cache.TryGetValue<List<string>>(cacheKey, out var cached) && cached is not null)
            return cached;

        var url = $"/rest/autocomplete/compound/{Uri.EscapeDataString(query)}/json?limit={Math.Clamp(limit, 1, 20)}";
        using var doc = await GetJsonAsync(url, cancellationToken);
        if (doc is null) return Array.Empty<string>();

        var names = doc.RootElement
            .TryGetProperty("dictionary_terms", out var terms) &&
            terms.TryGetProperty("compound", out var compounds)
                ? compounds.EnumerateArray().Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                : new List<string>();

        _cache.Set(cacheKey, names, SuggestCacheTtl);
        return names;
    }

    public async Task<ChemicalDto?> ResolveByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (name.Length < 2) return null;

        // 1. Точное попадание в локальном каталоге (по каноническому имени, регистр игнорируем).
        var cachedByName = await _db.ChemicalCatalog
            .FirstOrDefaultAsync(e => e.CanonicalName.ToLower() == name.ToLower(), cancellationToken);
        if (cachedByName is not null) return ToDto(cachedByName);

        // 2. Запрос свойств в PubChem по названию (включая синонимы).
        var propsUrl = $"/rest/pug/compound/name/{Uri.EscapeDataString(name)}/property/Title,MolecularFormula,MolecularWeight,CanonicalSMILES/JSON";
        using var propsDoc = await GetJsonAsync(propsUrl, cancellationToken);
        if (propsDoc is null ||
            !propsDoc.RootElement.TryGetProperty("PropertyTable", out var table) ||
            !table.TryGetProperty("Properties", out var properties) ||
            properties.GetArrayLength() == 0)
        {
            return null;
        }

        var p = properties[0];
        var cid = p.GetProperty("CID").GetInt32();
        var title = p.TryGetProperty("Title", out var t) ? t.GetString() ?? name : name;
        var formula = p.TryGetProperty("MolecularFormula", out var f) ? f.GetString() : null;
        var smiles = p.TryGetProperty("CanonicalSMILES", out var s) ? s.GetString() : null;
        var molarMass = p.TryGetProperty("MolecularWeight", out var mw) && double.TryParse(mw.GetString(),
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

        // 3. Вещество могло быть закэшировано раньше под другим именем (синонимом) — CID совпадёт.
        //    Дозаполняем SMILES для записей, созданных до появления структурных полей.
        var existing = await _db.ChemicalCatalog.FirstOrDefaultAsync(e => e.PubChemCid == cid, cancellationToken);
        if (existing is not null)
        {
            var changed = false;
            if (existing.Smiles is null && smiles is not null) { existing.Smiles = smiles; changed = true; }
            if (existing.Formula is null && formula is not null) { existing.Formula = formula; changed = true; }
            if (existing.CasNumber is null)
            {
                try { existing.CasNumber = await FetchPrimaryCasAsync(cid, cancellationToken); changed = true; }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to fetch synonyms/CAS for CID {Cid}", cid); }
            }
            if (changed) await _db.SaveChangesAsync(cancellationToken);
            return ToDto(existing);
        }

        // 4. CAS-номер берём из списка синонимов (доп. запрос, только при первом резолве).
        string? cas = null;
        try
        {
            cas = await FetchPrimaryCasAsync(cid, cancellationToken);
        }
        catch (Exception ex)
        {
            // Отсутствие CAS не должно ломать резолв — это справочная информация.
            _logger.LogWarning(ex, "Failed to fetch synonyms/CAS for CID {Cid}", cid);
        }

        // 5. Кэшируем запись в БД.
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
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(entry);
    }

    public async Task<IReadOnlyList<ChemicalDto>> GetByCidsAsync(IReadOnlyList<int> cids, CancellationToken cancellationToken = default)
    {
        if (cids.Count == 0) return Array.Empty<ChemicalDto>();
        return await _db.ChemicalCatalog
            .Where(e => cids.Contains(e.PubChemCid))
            .Select(e => new ChemicalDto(e.PubChemCid, e.CanonicalName, e.CasNumber, e.Formula, e.MolarMass, e.Smiles))
            .ToListAsync(cancellationToken);
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
