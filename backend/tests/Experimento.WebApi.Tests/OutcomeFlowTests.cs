using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Experimento.Domain.Entities;
using Experimento.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Experimento.WebApi.Tests;

/// <summary>
/// Lab Journal контур: review + лабораторный исход (upsert), читаемость их в результате,
/// влияние на calibration и model scorecard, изоляция между пользователями.
/// Плюс сравнение версий формуляции.
/// </summary>
public class OutcomeFlowTests : IClassFixture<ApiFixture>
{
    private readonly HttpClient _client;
    private readonly ApiFixture _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public OutcomeFlowTests(ApiFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact(DisplayName = "Review and lab outcome: validation, upsert, read-back, calibration, scorecard, isolation")]
    public async Task ReviewAndOutcome_Flow_WorksEndToEnd()
    {
        var setup = await CreatePredictionAsync();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", setup.AccessToken);

        // --- Review: валидное решение принимается, мусорное — отклоняется ---
        var reviewResp = await _client.PostAsJsonAsync(
            $"/api/predictions/prediction-results/{setup.ResultId}/review",
            new { decision = "Approved", comment = "Lab agrees with prediction." });
        Assert.True(reviewResp.IsSuccessStatusCode, $"Review failed: {reviewResp.StatusCode}");

        var badReview = await _client.PostAsJsonAsync(
            $"/api/predictions/prediction-results/{setup.ResultId}/review",
            new { decision = "Maybe", comment = "" });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, badReview.StatusCode);

        // --- Outcome: мусорный JSON отклоняется ---
        var badOutcome = await _client.PostAsJsonAsync(
            $"/api/predictions/prediction-results/{setup.ResultId}/outcome",
            new { actualSuccess = true, actualMetricsJson = "not-json", notes = "" });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, badOutcome.StatusCode);

        // --- Outcome: первая запись ---
        var outcomeResp = await _client.PostAsJsonAsync(
            $"/api/predictions/prediction-results/{setup.ResultId}/outcome",
            new
            {
                actualSuccess = true,
                actualMetricsJson = """{"toxicity":0.2,"stability":0.8}""",
                notes = "First lab run"
            });
        Assert.True(outcomeResp.IsSuccessStatusCode, $"Outcome failed: {outcomeResp.StatusCode}");

        // --- Повторная запись того же результата — это upsert, а не 500 (unique index) ---
        var updateResp = await _client.PostAsJsonAsync(
            $"/api/predictions/prediction-results/{setup.ResultId}/outcome",
            new
            {
                actualSuccess = false,
                actualMetricsJson = """{"toxicity":0.7}""",
                notes = "Corrected after repeat synthesis"
            });
        Assert.True(updateResp.IsSuccessStatusCode, $"Outcome update failed: {updateResp.StatusCode}");
        var updated = await updateResp.Content.ReadFromJsonAsync<OutcomeBody>(JsonOpts);
        Assert.NotNull(updated);
        Assert.False(updated.ActualSuccess);
        Assert.Equal("Corrected after repeat synthesis", updated.Notes);

        // --- Read-back: результат содержит и review, и актуальный исход ---
        var resResp = await _client.GetAsync($"/api/predictions/prediction-jobs/{setup.JobId}/result");
        Assert.True(resResp.IsSuccessStatusCode);
        var result = await resResp.Content.ReadFromJsonAsync<ResultBody>(JsonOpts);
        Assert.NotNull(result);
        Assert.Contains(result.Reviews, r => r.Decision == "Approved");
        Assert.NotNull(result.Outcome);
        Assert.False(result.Outcome.ActualSuccess);

        // --- Калибровка учитывает исход (у свежего пользователя ровно один) ---
        var calResp = await _client.GetAsync("/api/predictions/calibration");
        Assert.True(calResp.IsSuccessStatusCode);
        var cal = await calResp.Content.ReadFromJsonAsync<CalibrationBody>(JsonOpts);
        Assert.NotNull(cal);
        Assert.Equal(1, cal.WithOutcome);
        Assert.True(cal.Total >= 1);

        // --- Scorecard: модель, давшая прогноз, имеет исход и ненулевую ошибку ---
        var scoreResp = await _client.GetAsync("/api/models/scorecard");
        Assert.True(scoreResp.IsSuccessStatusCode);
        var scorecard = await scoreResp.Content.ReadFromJsonAsync<List<ScorecardRow>>(JsonOpts);
        Assert.NotNull(scorecard);
        Assert.Contains(scorecard, s => s.WithOutcome >= 1);
        var ourModel = scorecard.Single(s => s.ModelId == setup.ModelRegistrationId);
        Assert.True(ourModel.MeanError > 0); // прогноз ≠ 0, а фактический исход = false

        // --- Изоляция: чужой пользователь не видит исход и не может его записать ---
        var otherEmail = $"other_{Guid.NewGuid():N}@experimento.test";
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email = otherEmail, password = "E2eTest12345!", displayName = "Other User" });
        var otherLogin = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = otherEmail, password = "E2eTest12345!" });
        var otherAuth = await otherLogin.Content.ReadFromJsonAsync<AuthBody>(JsonOpts);
        var otherClient = _factory.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", otherAuth!.AccessToken);

        var foreignResult = await otherClient.GetAsync($"/api/predictions/prediction-jobs/{setup.JobId}/result");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, foreignResult.StatusCode);

        var foreignOutcome = await otherClient.PostAsJsonAsync(
            $"/api/predictions/prediction-results/{setup.ResultId}/outcome",
            new { actualSuccess = true, actualMetricsJson = "{}", notes = "intruder" });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, foreignOutcome.StatusCode);

        var otherScore = await otherClient.GetAsync("/api/models/scorecard");
        var otherCard = await otherScore.Content.ReadFromJsonAsync<List<ScorecardRow>>(JsonOpts);
        Assert.NotNull(otherCard);
        Assert.All(otherCard, s => Assert.Equal(0, s.WithOutcome));
    }

    [Fact(DisplayName = "Compare versions: Added/Removed/Modified component diffs and 403 for foreign formulation")]
    public async Task CompareVersions_ReturnsDiffs()
    {
        // Дополнительно засеваем глюкозу в локальный каталог, чтобы не зависеть от PubChem.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!db.ChemicalCatalog.Any(e => e.PubChemCid == GlucoseCid))
            {
                db.ChemicalCatalog.Add(new ChemicalCatalogEntry
                {
                    PubChemCid = GlucoseCid, CanonicalName = "Glucose", CasNumber = "50-99-7",
                    Formula = "C6H12O6", MolarMass = 180.16, Smiles = "C(C1C(C(C(C(O1)O)O)O)O)O"
                });
                db.SaveChanges();
            }
        }

        var email = $"cmp_{Guid.NewGuid():N}@experimento.test";
        var reg = await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "E2eTest12345!", displayName = "Compare User" });
        var auth = await reg.Content.ReadFromJsonAsync<AuthBody>(JsonOpts);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var project = await PostJson<IdBody>("/api/projects", new { name = "Compare Project" });
        var formulation = await PostJson<IdBody>("/api/formulations",
            new { projectId = project.Id, name = "Compare Form", targetPurpose = "diff test" });
        var formulationId = formulation.Id;

        var v1Body = new
        {
            formulationId,
            components = new[]
            {
                new { chemicalName = "Aspirin", molarMass = 180.16, proportion = 0.6, role = "Active",
                      casNumber = "50-78-2", formula = "C9H8O4", pubChemCid = ApiFixture.AspirinCid },
                new { chemicalName = "Sodium chloride", molarMass = 58.44, proportion = 0.4, role = "Excipient",
                      casNumber = "7647-14-5", formula = "ClNa", pubChemCid = ApiFixture.SodiumChlorideCid }
            },
            conditions = new { temperatureCelsius = 25.0 }
        };
        var v1 = await PostJson<IdBody>($"/api/formulations/{formulationId}/versions", v1Body);

        // v2: аспирин изменён (0.7), хлорид убран, глюкоза добавлена (0.3).
        var v2Body = new
        {
            formulationId,
            components = new[]
            {
                new { chemicalName = "Aspirin", molarMass = 180.16, proportion = 0.7, role = "Active",
                      casNumber = "50-78-2", formula = "C9H8O4", pubChemCid = ApiFixture.AspirinCid },
                new { chemicalName = "Glucose", molarMass = 180.16, proportion = 0.3, role = "Stabilizer",
                      casNumber = "50-99-7", formula = "C6H12O6", pubChemCid = GlucoseCid }
            },
            conditions = new { temperatureCelsius = 25.0 }
        };
        var v2 = await PostJson<IdBody>($"/api/formulations/{formulationId}/versions", v2Body);

        var cmpResp = await _client.GetAsync($"/api/formulations/{formulationId}/compare?a={v1.Id}&b={v2.Id}");
        Assert.True(cmpResp.IsSuccessStatusCode, $"Compare failed: {cmpResp.StatusCode}");
        var cmp = await cmpResp.Content.ReadFromJsonAsync<ComparisonBody>(JsonOpts);
        Assert.NotNull(cmp);
        Assert.Equal(3, cmp.ComponentDiffs.Count);

        var aspirin = cmp.ComponentDiffs.Single(d => d.ChemicalName == "Aspirin");
        Assert.Equal("Modified", aspirin.Change);
        Assert.Equal(0.6, aspirin.ProportionA);
        Assert.Equal(0.7, aspirin.ProportionB);

        var removed = cmp.ComponentDiffs.Single(d => d.ChemicalName == "Sodium chloride");
        Assert.Equal("Removed", removed.Change);
        Assert.Equal(0.4, removed.ProportionA);
        Assert.Null(removed.ProportionB);

        var added = cmp.ComponentDiffs.Single(d => d.ChemicalName == "Glucose");
        Assert.Equal("Added", added.Change);
        Assert.Null(added.ProportionA);
        Assert.Equal(0.3, added.ProportionB);

        // Чужая формуляция не сравнивается.
        var otherEmail = $"cmpother_{Guid.NewGuid():N}@experimento.test";
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email = otherEmail, password = "E2eTest12345!", displayName = "Other" });
        var otherLogin = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = otherEmail, password = "E2eTest12345!" });
        var otherAuth = await otherLogin.Content.ReadFromJsonAsync<AuthBody>(JsonOpts);
        var otherClient = _factory.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", otherAuth!.AccessToken);
        var foreignCmp = await otherClient.GetAsync(
            $"/api/formulations/{formulationId}/compare?a={v1.Id}&b={v2.Id}");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, foreignCmp.StatusCode);
    }

    private const int GlucoseCid = 5793;

    private async Task<TBody> PostJson<TBody>(string url, object body)
    {
        var resp = await _client.PostAsJsonAsync(url, body);
        Assert.True(resp.IsSuccessStatusCode, $"POST {url} failed: {resp.StatusCode}");
        var parsed = await resp.Content.ReadFromJsonAsync<TBody>(JsonOpts);
        Assert.NotNull(parsed);
        return parsed!;
    }

    private async Task<(string AccessToken, Guid JobId, Guid ResultId, Guid ModelRegistrationId)> CreatePredictionAsync()
    {
        var email = $"out_{Guid.NewGuid():N}@experimento.test";
        var regResp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "E2eTest12345!", displayName = "Outcome User" });
        var reg = await regResp.Content.ReadFromJsonAsync<AuthBody>(JsonOpts);
        var token = reg!.AccessToken;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var project = await PostJson<IdBody>("/api/projects", new { name = "Outcome Project" });
        var formulation = await PostJson<IdBody>("/api/formulations",
            new { projectId = project.Id, name = "Outcome Form", targetPurpose = "outcome test" });

        var version = await PostJson<IdBody>($"/api/formulations/{formulation.Id}/versions", new
        {
            formulationId = formulation.Id,
            components = new[]
            {
                new { chemicalName = "Aspirin", molarMass = 180.16, proportion = 0.6, role = "Active",
                      casNumber = "50-78-2", formula = "C9H8O4", pubChemCid = ApiFixture.AspirinCid },
                new { chemicalName = "Sodium chloride", molarMass = 58.44, proportion = 0.4, role = "Excipient",
                      casNumber = "7647-14-5", formula = "ClNa", pubChemCid = ApiFixture.SodiumChlorideCid }
            },
            conditions = new { temperatureCelsius = 25.0, phTarget = 7.0, solvent = "water" }
        });

        var jobResp = await _client.PostAsync(
            $"/api/predictions/formulation-versions/{version.Id}/predictions", null);
        Assert.True(jobResp.IsSuccessStatusCode, $"Submit prediction failed: {jobResp.StatusCode}");
        var job = await jobResp.Content.ReadFromJsonAsync<IdBody>(JsonOpts);
        Assert.NotNull(job);

        // Результат сохраняется до финального статуса job — поллим до его появления.
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(1000);
            var res = await _client.GetAsync($"/api/predictions/prediction-jobs/{job.Id}/result");
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadFromJsonAsync<ResultBody>(JsonOpts);
                if (body is not null)
                    return (token, Guid.Parse(job.Id), Guid.Parse(body.Id), Guid.Parse(body.ModelRegistrationId));
            }
        }
        throw new InvalidOperationException("Prediction result did not appear in time.");
    }

    // --- DTO для десериализации ---
    private record AuthBody(UserBody User, string AccessToken);
    private record UserBody(string Id, string Email, string DisplayName, string Role);
    private record IdBody(string Id);
    private record ReviewBody(string Id, string Decision, string? Comment);
    private record OutcomeBody(string Id, bool ActualSuccess, string ActualMetricsJson, string? Notes);
    private record ResultBody(
        string Id, string JobId, string ModelRegistrationId,
        List<ReviewBody> Reviews, OutcomeBody? Outcome);
    private record CalibrationBody(int Total, int WithOutcome, double MeanError, double MeanBias);
    private record ScorecardRow(Guid ModelId, int Total, int WithOutcome, double MeanError, double MeanBias);
    private record ComparisonBody(VersionSide VersionA, VersionSide VersionB, List<DiffBody> ComponentDiffs);
    private record VersionSide(int VersionNumber);
    private record DiffBody(string ChemicalName, string Change, double? ProportionA, double? ProportionB);
}
