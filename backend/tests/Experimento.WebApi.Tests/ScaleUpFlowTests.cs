using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Experimento.WebApi.Tests;

/// <summary>
/// Оценка масштабирования версии: контракт эндпоинта, сериализация серьёзности строкой
/// и разграничение доступа к чужим версиям.
/// </summary>
public class ScaleUpFlowTests : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly ApiFixture _factory;
    private readonly HttpClient _client;

    public ScaleUpFlowTests(ApiFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>Свежий пользователь с проектом, формуляцией и одной версией на аспирине.</summary>
    private async Task<Guid> CreateVersionAsync(HttpClient client)
    {
        var email = $"scaleup_{Guid.NewGuid():N}@experimento.test";
        var register = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "E2eTest12345!", displayName = "Scale-up User" });
        Assert.True(register.IsSuccessStatusCode, $"Register failed: {register.StatusCode}");
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOpts);
        Assert.NotNull(auth);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var project = await (await client.PostAsJsonAsync("/api/projects",
            new { name = "Scale-up project", description = "e2e" })).Content.ReadFromJsonAsync<IdBody>(JsonOpts);
        var formulation = await (await client.PostAsJsonAsync("/api/formulations",
            new { projectId = project!.Id, name = "Scale-up formulation", targetPurpose = "e2e" }))
            .Content.ReadFromJsonAsync<IdBody>(JsonOpts);

        var versionResp = await client.PostAsJsonAsync($"/api/formulations/{formulation!.Id}/versions", new
        {
            components = new[]
            {
                new { chemicalName = "Aspirin", formula = "C9H8O4", molarMass = 180.16,
                      proportion = 1.0, role = "Active", pubChemCid = ApiFixture.AspirinCid }
            },
            conditions = new
            {
                temperatureCelsius = 25.0, pressureKPa = 101.3, phTarget = 7.0, solvent = "water"
            },
            notes = "scale-up e2e"
        });
        Assert.True(versionResp.IsSuccessStatusCode, $"Create version failed: {versionResp.StatusCode}");
        var version = await versionResp.Content.ReadFromJsonAsync<IdBody>(JsonOpts);
        return version!.Id;
    }

    [Fact(DisplayName = "A clean version at laboratory scale scores 100 and reports no findings")]
    public async Task ScaleUp_LabScale_IsClean()
    {
        var versionId = await CreateVersionAsync(_client);

        var resp = await _client.GetAsync($"/api/formulations/versions/{versionId}/scale-up?targetVolumeLitres=1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ScaleUpBody>(JsonOpts);
        Assert.NotNull(body);
        Assert.Equal(100, body!.ReadinessScore);
        Assert.Equal("Ready for pilot scale", body.Verdict);
        Assert.Equal(1.0, body.LabReferenceVolumeLitres, 3);
        Assert.Equal(1.0, body.ScaleFactor, 3);
        Assert.Empty(body.Findings);
    }

    [Fact(DisplayName = "Severity is serialised as a string and cooling loss is reported at pilot scale")]
    public async Task ScaleUp_PilotScale_ReportsCoolingLoss()
    {
        var versionId = await CreateVersionAsync(_client);

        var resp = await _client.GetAsync($"/api/formulations/versions/{versionId}/scale-up?targetVolumeLitres=50");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Разбор сырого JSON: серьёзность обязана быть строкой, а не числом.
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var findings = doc.RootElement.GetProperty("findings").EnumerateArray().ToList();
        var thermal = findings.Single(f => f.GetProperty("factor").GetString() == "Thermal");

        Assert.Equal("Medium", thermal.GetProperty("severity").GetString());
        Assert.Contains("cooling capacity per litre", thermal.GetProperty("observation").GetString());
        Assert.Equal(92, doc.RootElement.GetProperty("readinessScore").GetInt32());
        Assert.Equal(50.0, doc.RootElement.GetProperty("scaleFactor").GetDouble(), 3);
    }

    [Theory(DisplayName = "Target volume outside 1..10000 litres is rejected")]
    [InlineData(0.0)]
    [InlineData(20000.0)]
    public async Task ScaleUp_InvalidVolume_BadRequest(double volume)
    {
        var versionId = await CreateVersionAsync(_client);

        var resp = await _client.GetAsync($"/api/formulations/versions/{versionId}/scale-up?targetVolumeLitres={volume}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact(DisplayName = "Unknown version returns 404")]
    public async Task ScaleUp_UnknownVersion_NotFound()
    {
        await CreateVersionAsync(_client);

        var resp = await _client.GetAsync($"/api/formulations/versions/{Guid.NewGuid()}/scale-up");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact(DisplayName = "Another user's version returns 403")]
    public async Task ScaleUp_ForeignVersion_Forbidden()
    {
        var versionId = await CreateVersionAsync(_client);

        var other = _factory.CreateClient();
        var email = $"scaleup_other_{Guid.NewGuid():N}@experimento.test";
        var auth = await (await other.PostAsJsonAsync("/api/auth/register",
                new { email, password = "E2eTest12345!", displayName = "Other User" }))
            .Content.ReadFromJsonAsync<AuthResponse>(JsonOpts);
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var resp = await other.GetAsync($"/api/formulations/versions/{versionId}/scale-up");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact(DisplayName = "Scale-up endpoint requires authentication")]
    public async Task ScaleUp_Anonymous_Unauthorized()
    {
        var versionId = await CreateVersionAsync(_client);

        var anonymous = _factory.CreateClient();
        var resp = await anonymous.GetAsync($"/api/formulations/versions/{versionId}/scale-up");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    private record AuthResponse(UserBody User, string AccessToken);
    private record UserBody(string Id, string Email, string DisplayName, string Role);
    private record IdBody(Guid Id);
    private record FindingBody(string Factor, string Severity, string Observation, string Recommendation);
    private record ScaleUpBody(
        Guid VersionId, int VersionNumber, double LabReferenceVolumeLitres, double TargetVolumeLitres,
        double ScaleFactor, int ReadinessScore, string Verdict, List<FindingBody> Findings);
}
