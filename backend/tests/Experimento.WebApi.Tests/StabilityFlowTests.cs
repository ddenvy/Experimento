using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Experimento.WebApi.Tests;

/// <summary>
/// Исследования стабильности: контракт эндпоинтов, расчёт срока годности по введённым данным,
/// валидация точек, сериализация уверенности строкой и разграничение доступа.
/// </summary>
public class StabilityFlowTests : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly ApiFixture _factory;
    private readonly HttpClient _client;

    public StabilityFlowTests(ApiFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>Точки, рассчитанные по первому порядку с k(25 °C) = 0.0005/сутки и Ea = 80 кДж/моль.</summary>
    private static List<object> SyntheticPoints()
    {
        var points = new List<object>();
        foreach (var temperature in new[] { 25.0, 40.0, 50.0 })
        {
            var kelvin = temperature + 273.15;
            var rate = 0.0005 * Math.Exp(-80_000.0 / 8.314 * (1 / kelvin - 1 / 298.15));
            foreach (var time in new[] { 0.0, 30.0, 60.0, 90.0 })
                points.Add(new
                {
                    temperatureCelsius = temperature,
                    timeDays = time,
                    assayPercent = Math.Round(100.0 * Math.Exp(-rate * time), 6)
                });
        }
        return points;
    }

    [Fact(DisplayName = "A study is stored, listed and turned into a shelf life projection")]
    public async Task StabilityStudy_ProducesShelfLifeProjection()
    {
        var versionId = await CreateVersionAsync();

        var create = await _client.PostAsJsonAsync($"/api/formulations/versions/{versionId}/stability-studies",
            new { points = SyntheticPoints(), notes = "accelerated study" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var study = await create.Content.ReadFromJsonAsync<StudyBody>(JsonOpts);
        Assert.NotNull(study);
        Assert.Equal(versionId, study!.VersionId);
        Assert.Equal(1, study.VersionNumber);
        Assert.Equal("accelerated study", study.Notes);
        Assert.Equal(12, study.Points.Count);
        // Точки отдаются упорядоченными по температуре, затем по времени.
        Assert.Equal(25, study.Points[0].TemperatureCelsius);
        Assert.Equal(0, study.Points[0].TimeDays);

        var list = await (await _client.GetAsync($"/api/formulations/versions/{versionId}/stability-studies"))
            .Content.ReadFromJsonAsync<List<StudyBody>>(JsonOpts);
        Assert.NotNull(list);
        Assert.Single(list!);

        var assessmentResp = await _client.GetAsync($"/api/formulations/stability-studies/{study.Id}/assessment");
        Assert.Equal(HttpStatusCode.OK, assessmentResp.StatusCode);

        // Разбор сырого JSON: уверенность обязана быть строкой, а не числом.
        using var doc = JsonDocument.Parse(await assessmentResp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.InRange(root.GetProperty("activationEnergyKjPerMol").GetDouble(), 79.5, 80.5);
        Assert.InRange(root.GetProperty("shelfLifeDaysAt25C").GetDouble(), 209, 213);
        Assert.Equal("High", root.GetProperty("confidence").GetString());
        Assert.Empty(root.GetProperty("warnings").EnumerateArray());
        Assert.Equal(3, root.GetProperty("rates").GetArrayLength());
        Assert.Contains("months at 25 °C", root.GetProperty("summary").GetString()!);
        Assert.NotEmpty(root.GetProperty("assumptions").EnumerateArray());
    }

    [Fact(DisplayName = "A study can be deleted together with its points")]
    public async Task StabilityStudy_CanBeDeleted()
    {
        var versionId = await CreateVersionAsync();
        var study = await (await _client.PostAsJsonAsync(
                $"/api/formulations/versions/{versionId}/stability-studies",
                new { points = SyntheticPoints() }))
            .Content.ReadFromJsonAsync<StudyBody>(JsonOpts);

        var delete = await _client.DeleteAsync($"/api/formulations/stability-studies/{study!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var list = await (await _client.GetAsync($"/api/formulations/versions/{versionId}/stability-studies"))
            .Content.ReadFromJsonAsync<List<StudyBody>>(JsonOpts);
        Assert.Empty(list!);

        var assessment = await _client.GetAsync($"/api/formulations/stability-studies/{study.Id}/assessment");
        Assert.Equal(HttpStatusCode.NotFound, assessment.StatusCode);
    }

    [Theory(DisplayName = "Invalid measurement points are rejected")]
    [InlineData(0.0)]
    [InlineData(105.0)]
    public async Task CreateStudy_InvalidAssay_BadRequest(double assay)
    {
        var versionId = await CreateVersionAsync();

        var resp = await _client.PostAsJsonAsync($"/api/formulations/versions/{versionId}/stability-studies",
            new { points = new[] { new { temperatureCelsius = 40.0, timeDays = 30.0, assayPercent = assay } } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact(DisplayName = "A study without points and an impossible temperature are rejected")]
    public async Task CreateStudy_InvalidShape_BadRequest()
    {
        var versionId = await CreateVersionAsync();

        var empty = await _client.PostAsJsonAsync($"/api/formulations/versions/{versionId}/stability-studies",
            new { points = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var hot = await _client.PostAsJsonAsync($"/api/formulations/versions/{versionId}/stability-studies",
            new { points = new[] { new { temperatureCelsius = 500.0, timeDays = 30.0, assayPercent = 95.0 } } });
        Assert.Equal(HttpStatusCode.BadRequest, hot.StatusCode);
    }

    [Fact(DisplayName = "An unknown version returns 404")]
    public async Task CreateStudy_UnknownVersion_NotFound()
    {
        await CreateVersionAsync();

        var resp = await _client.PostAsJsonAsync($"/api/formulations/versions/{Guid.NewGuid()}/stability-studies",
            new { points = SyntheticPoints() });

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact(DisplayName = "Another user's study returns 403")]
    public async Task StabilityStudy_ForeignOwner_Forbidden()
    {
        var versionId = await CreateVersionAsync();
        var study = await (await _client.PostAsJsonAsync(
                $"/api/formulations/versions/{versionId}/stability-studies",
                new { points = SyntheticPoints() }))
            .Content.ReadFromJsonAsync<StudyBody>(JsonOpts);

        var other = _factory.CreateClient();
        var auth = await (await other.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"stability_other_{Guid.NewGuid():N}@experimento.test",
            password = "E2eTest12345!",
            displayName = "Other User"
        })).Content.ReadFromJsonAsync<AuthResponse>(JsonOpts);
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var assessment = await other.GetAsync($"/api/formulations/stability-studies/{study!.Id}/assessment");
        Assert.Equal(HttpStatusCode.Forbidden, assessment.StatusCode);

        var delete = await other.DeleteAsync($"/api/formulations/stability-studies/{study.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact(DisplayName = "Stability endpoints require authentication")]
    public async Task Stability_Anonymous_Unauthorized()
    {
        var versionId = await CreateVersionAsync();

        var anonymous = _factory.CreateClient();
        var resp = await anonymous.GetAsync($"/api/formulations/versions/{versionId}/stability-studies");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    /// <summary>Свежий пользователь с проектом, формуляцией и одной версией из каталога.</summary>
    private async Task<Guid> CreateVersionAsync()
    {
        var auth = await (await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"stability_{Guid.NewGuid():N}@experimento.test",
            password = "E2eTest12345!",
            displayName = "Stability User"
        })).Content.ReadFromJsonAsync<AuthResponse>(JsonOpts);
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var project = await (await _client.PostAsJsonAsync("/api/projects",
                new { name = "Stability project", description = "e2e" }))
            .Content.ReadFromJsonAsync<IdBody>(JsonOpts);
        var formulation = await (await _client.PostAsJsonAsync("/api/formulations",
                new { projectId = project!.Id, name = "Stability formulation", targetPurpose = "e2e" }))
            .Content.ReadFromJsonAsync<IdBody>(JsonOpts);

        var version = await (await _client.PostAsJsonAsync($"/api/formulations/{formulation!.Id}/versions", new
        {
            components = new[]
            {
                new { chemicalName = "Aspirin", formula = "C9H8O4", molarMass = 180.16,
                      proportion = 1.0, role = "Active", pubChemCid = ApiFixture.AspirinCid }
            },
            conditions = new { temperatureCelsius = 25.0, phTarget = 7.0, solvent = "water" }
        })).Content.ReadFromJsonAsync<IdBody>(JsonOpts);

        return version!.Id;
    }

    private record AuthResponse(UserBody User, string AccessToken);
    private record UserBody(Guid Id, string Email, string DisplayName, string Role);
    private record IdBody(Guid Id);
    private record PointBody(double TemperatureCelsius, double TimeDays, double AssayPercent);
    private record StudyBody(
        Guid Id, Guid VersionId, int VersionNumber, string? Notes, DateTime CreatedAtUtc, List<PointBody> Points);
}
