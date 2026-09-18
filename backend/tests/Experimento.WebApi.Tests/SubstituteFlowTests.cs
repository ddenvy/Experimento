using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Experimento.Domain.Entities;
using Experimento.Domain.Enums;
using Experimento.Infrastructure.Data;
using Experimento.WebApi.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Experimento.WebApi.Tests;

/// <summary>
/// Подбор замен компонента: контракт эндпоинта и сквозная проверка ранжирования
/// на контролируемом наборе каталога (изомер vs запрещённый аналог).
/// </summary>
public class SubstituteFlowTests : IClassFixture<ApiFixture>
{
    // Собственные CID, чтобы не пересекаться с реальными записями каталога и с другими тестами.
    private const int TargetCid = 990001;
    private const int CompliantIsomerCid = 990002;
    private const int BannedIsomerCid = 990003;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly ApiFixture _factory;
    private readonly HttpClient _client;

    public SubstituteFlowTests(ApiFixture factory)
    {
        _factory = factory;
        SeedCatalog(factory);
        _client = factory.CreateClient();
        AuthenticateAsync(_client).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Три вещества с одной брутто-формулой (изомеры): цель, «чистый» аналог и аналог
    /// с запретом. Так ранжирование проверяется независимо от остального каталога.
    /// </summary>
    private static void SeedCatalog(ApiFixture factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var seeds = new[]
        {
            new ChemicalCatalogEntry
            {
                PubChemCid = TargetCid, CanonicalName = "E2E Substitute Target", CasNumber = "65-85-0",
                Formula = "C7H6O2", MolarMass = 122.12, Smiles = "O=C(O)c1ccccc1"
            },
            new ChemicalCatalogEntry
            {
                PubChemCid = CompliantIsomerCid, CanonicalName = "E2E Compliant Analogue",
                Formula = "C7H6O2", MolarMass = 122.12, Smiles = "O=Cc1ccc(O)cc1"
            },
            new ChemicalCatalogEntry
            {
                PubChemCid = BannedIsomerCid, CanonicalName = "E2E Banned Analogue",
                Formula = "C7H6O2", MolarMass = 122.12, Smiles = "O=Cc1ccccc1O"
            },
        };

        var added = false;
        foreach (var seed in seeds)
        {
            if (db.ChemicalCatalog.Any(e => e.PubChemCid == seed.PubChemCid)) continue;
            db.ChemicalCatalog.Add(seed);
            added = true;
        }
        if (added) db.SaveChanges();

        var banned = db.ChemicalCatalog.First(e => e.PubChemCid == BannedIsomerCid);
        if (!db.ChemicalRegulations.Any(r => r.ChemicalCatalogEntryId == banned.Id))
        {
            db.ChemicalRegulations.Add(new ChemicalRegulation
            {
                ChemicalCatalogEntryId = banned.Id,
                Authority = RegulationAuthority.EpaPfas,
                Status = RegulationStatus.Banned,
                Reason = "E2E seeded restriction for substitute ranking test",
            });
            db.SaveChanges();
        }
    }

    [Fact(DisplayName = "Substitutes endpoint ranks a compliant isomer above a banned analogue")]
    public async Task GetSubstitutes_RanksCompliantIsomerFirst()
    {
        var resp = await _client.GetAsync($"/api/chemicals/{TargetCid}/substitutes?limit=25");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var items = doc.RootElement.EnumerateArray().ToList();
        Assert.NotEmpty(items);

        // Цель не предлагается сама себе.
        Assert.DoesNotContain(items, i => i.GetProperty("pubChemCid").GetInt32() == TargetCid);

        var compliant = items.Single(i => i.GetProperty("pubChemCid").GetInt32() == CompliantIsomerCid);
        var banned = items.Single(i => i.GetProperty("pubChemCid").GetInt32() == BannedIsomerCid);

        // Изомер: полное совпадение свойств, статус отдаётся строкой.
        Assert.Equal(1.0, compliant.GetProperty("similarity").GetDouble(), 4);
        Assert.Equal("Compliant", compliant.GetProperty("regulatoryStatus").GetString());
        Assert.Contains("same molecular formula (isomer)",
            compliant.GetProperty("matchedSignals").EnumerateArray().Select(s => s.GetString()));

        // Запрещённый аналог структурно идентичен, но его ранжирующий балл срезан штрафом.
        Assert.Equal("Banned", banned.GetProperty("regulatoryStatus").GetString());
        Assert.Equal(1.0, banned.GetProperty("similarity").GetDouble(), 4);
        Assert.True(banned.GetProperty("matchScore").GetDouble() < compliant.GetProperty("matchScore").GetDouble());

        // Сортировка строго по убыванию ранжирующего балла.
        var scores = items.Select(i => i.GetProperty("matchScore").GetDouble()).ToList();
        Assert.Equal(scores.OrderByDescending(s => s), scores);
    }

    [Fact(DisplayName = "Substitutes endpoint honours the limit parameter")]
    public async Task GetSubstitutes_HonoursLimit()
    {
        var resp = await _client.GetAsync($"/api/chemicals/{TargetCid}/substitutes?limit=1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var items = await resp.Content.ReadFromJsonAsync<List<SubstituteBody>>(JsonOpts);
        Assert.NotNull(items);
        Assert.Single(items!);
    }

    [Theory(DisplayName = "Substitutes endpoint rejects an out-of-range limit")]
    [InlineData(0)]
    [InlineData(26)]
    public async Task GetSubstitutes_InvalidLimit_BadRequest(int limit)
    {
        var resp = await _client.GetAsync($"/api/chemicals/{TargetCid}/substitutes?limit={limit}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact(DisplayName = "Substitutes endpoint returns 404 for a chemical that is not in the catalog")]
    public async Task GetSubstitutes_UnknownChemical_NotFound()
    {
        var resp = await _client.GetAsync("/api/chemicals/987654321/substitutes");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact(DisplayName = "Substitutes endpoint requires authentication")]
    public async Task GetSubstitutes_Anonymous_Unauthorized()
    {
        var anonymous = _factory.CreateClient();
        var resp = await anonymous.GetAsync($"/api/chemicals/{TargetCid}/substitutes");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    private static async Task AuthenticateAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = DbSeeder.TestAdminEmail, password = DbSeeder.TestAdminPassword });
        var auth = await login.Content.ReadFromJsonAsync<AuthBody>(JsonOpts);
        Assert.NotNull(auth);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
    }

    private record AuthBody(UserBody User, string AccessToken);
    private record UserBody(string Id, string Email, string DisplayName, string Role);
    private record SubstituteBody(int PubChemCid, string Name, double Similarity, double MatchScore, string RegulatoryStatus);
}
