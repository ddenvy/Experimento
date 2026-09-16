using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Experimento.WebApi.Tests;

/// <summary>
/// Регуляторные статусы веществ: эндпоинты отдают статус по каждому органу и
/// агрегированный наивысший статус. Тесты фиксируют контракт, включая строковую
/// сериализацию enum'ов — фронтенд сопоставляет значения по имени, а не по числу.
/// </summary>
public class RegulationsFlowTests : IClassFixture<ApiFixture>
{
    // Formaldehyde присутствует в сид-справочнике (REACH SVHC + California Prop 65).
    private const int FormaldehydeCid = 712;

    private readonly HttpClient _client;
    private readonly ApiFixture _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public RegulationsFlowTests(ApiFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        AuthenticateAsync().GetAwaiter().GetResult();
    }

    [Fact(DisplayName = "Regulations endpoint returns string enum values for a flagged substance")]
    public async Task GetRegulations_FlaggedSubstance_ReturnsStringEnums()
    {
        var resp = await _client.GetAsync($"/api/chemicals/regulations/{FormaldehydeCid}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal(FormaldehydeCid, root.GetProperty("pubChemCid").GetInt32());
        // Наивысший статус сериализуется строкой — иначе UI получает числовой код и падает.
        Assert.Equal("Restricted", root.GetProperty("highestStatus").GetString());

        var regulations = root.GetProperty("regulations").EnumerateArray().ToList();
        Assert.NotEmpty(regulations);
        Assert.All(regulations, r =>
        {
            Assert.Equal("Restricted", r.GetProperty("status").GetString());
            Assert.False(string.IsNullOrWhiteSpace(r.GetProperty("reason").GetString()));
        });

        var authorities = regulations.Select(r => r.GetProperty("authority").GetString()).ToList();
        Assert.Contains("ReachSvhc", authorities);
        Assert.Contains("CaliforniaProp65", authorities);
    }

    [Fact(DisplayName = "Regulations endpoint reports a clean substance as Compliant with no entries")]
    public async Task GetRegulations_CleanSubstance_ReturnsCompliant()
    {
        var resp = await _client.GetAsync($"/api/chemicals/regulations/{ApiFixture.AspirinCid}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("Compliant", root.GetProperty("highestStatus").GetString());
        Assert.Empty(root.GetProperty("regulations").EnumerateArray());
    }

    [Fact(DisplayName = "Batch regulations endpoint returns one summary per requested CID")]
    public async Task GetRegulationsBatch_ReturnsSummaryPerCid()
    {
        var resp = await _client.PostAsJsonAsync("/api/chemicals/regulations/batch",
            new[] { FormaldehydeCid, ApiFixture.AspirinCid });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var summaries = await resp.Content.ReadFromJsonAsync<List<BatchSummary>>(JsonOpts);
        Assert.NotNull(summaries);
        Assert.Equal(2, summaries!.Count);

        Assert.Equal("Restricted", summaries.Single(s => s.PubChemCid == FormaldehydeCid).HighestStatus);
        Assert.Equal("Compliant", summaries.Single(s => s.PubChemCid == ApiFixture.AspirinCid).HighestStatus);
    }

    [Fact(DisplayName = "Regulations endpoint requires authentication")]
    public async Task GetRegulations_Anonymous_Unauthorized()
    {
        var anonymous = _factory.CreateClient();
        var resp = await anonymous.GetAsync($"/api/chemicals/regulations/{FormaldehydeCid}");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    private async Task AuthenticateAsync()
    {
        var email = $"reg_{Guid.NewGuid():N}@experimento.test";
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "E2eTest12345!", displayName = "Regulations User" });
        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "E2eTest12345!" });
        var auth = await login.Content.ReadFromJsonAsync<AuthBody>(JsonOpts);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
    }

    private record AuthBody(UserBody User, string AccessToken);
    private record UserBody(string Id, string Email, string DisplayName, string Role);
    private record BatchSummary(int PubChemCid, string HighestStatus);
}
