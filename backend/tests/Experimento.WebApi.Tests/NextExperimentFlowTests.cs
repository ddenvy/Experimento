using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Experimento.Domain.Entities;
using Experimento.Domain.Enums;
using Experimento.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Experimento.WebApi.Tests;

/// <summary>
/// План следующих экспериментов: контракт эндпоинта на подготовленной истории
/// (проверенная версия с исходом + версия с прогнозом и симуляцией без исхода),
/// сериализация уверенности строкой и разграничение доступа.
/// </summary>
public class NextExperimentFlowTests : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly ApiFixture _factory;
    private readonly HttpClient _client;

    public NextExperimentFlowTests(ApiFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact(DisplayName = "Plan ranks the untested simulation lead, the winning version and the open loop")]
    public async Task NextExperiments_RanksLeadRepeatAndLoop()
    {
        var setup = await SeedFormulationAsync(versionsCount: 2);
        SeedHistory(setup.UserId, validatedVersionId: setup.VersionIds[0], pendingVersionId: setup.VersionIds[1]);

        var resp = await _client.GetAsync($"/api/formulations/{setup.FormulationId}/next-experiments");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Разбираем сырой JSON: уверенность обязана быть строкой, а не числом.
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("versionsTotal").GetInt32());
        Assert.Equal(1, root.GetProperty("outcomesRecorded").GetInt32());
        Assert.Equal(0.2, root.GetProperty("meanCalibrationError").GetDouble(), 3);

        var items = root.GetProperty("recommendations").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);

        // 1. Непроверенный кандидат симуляции: уверенность средняя (один исход, ошибка 0.2).
        Assert.Equal("SimulationLead", items[0].GetProperty("kind").GetString());
        Assert.Equal("Medium", items[0].GetProperty("confidence").GetString());
        Assert.Equal(setup.VersionIds[1], items[0].GetProperty("versionId").GetGuid());

        var parameters = items[0].GetProperty("suggestedParameters").EnumerateArray().ToList();
        Assert.Equal(2, parameters.Count);
        Assert.Equal("temperatureCelsius", parameters[0].GetProperty("name").GetString());
        Assert.Equal(38.5, parameters[0].GetProperty("value").GetDouble(), 3);

        // 2. Версия, которая уже сработала в лаборатории.
        Assert.Equal("RepeatSuccess", items[1].GetProperty("kind").GetString());
        Assert.Equal("High", items[1].GetProperty("confidence").GetString());
        Assert.Equal(setup.VersionIds[0], items[1].GetProperty("versionId").GetGuid());

        // 3. Прогноз без лабораторного исхода — цикл открыт.
        Assert.Equal("CloseLoop", items[2].GetProperty("kind").GetString());
        Assert.Contains("Record the laboratory outcome", items[2].GetProperty("title").GetString()!);
    }

    [Fact(DisplayName = "A formulation without versions yields an empty plan")]
    public async Task NextExperiments_WithoutVersions_ReturnsEmptyPlan()
    {
        var setup = await SeedFormulationAsync(versionsCount: 0);

        var resp = await _client.GetAsync($"/api/formulations/{setup.FormulationId}/next-experiments");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<PlanBody>(JsonOpts);
        Assert.NotNull(body);
        Assert.Empty(body!.Recommendations);
        Assert.Equal(0, body.VersionsTotal);
        Assert.Null(body.MeanCalibrationError);
    }

    [Theory(DisplayName = "Limit outside 1..20 is rejected")]
    [InlineData(0)]
    [InlineData(21)]
    public async Task NextExperiments_InvalidLimit_BadRequest(int limit)
    {
        var setup = await SeedFormulationAsync(versionsCount: 0);

        var resp = await _client.GetAsync($"/api/formulations/{setup.FormulationId}/next-experiments?limit={limit}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact(DisplayName = "Another user's formulation returns 403")]
    public async Task NextExperiments_ForeignFormulation_Forbidden()
    {
        var setup = await SeedFormulationAsync(versionsCount: 0);

        var other = _factory.CreateClient();
        var auth = await (await other.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"next_other_{Guid.NewGuid():N}@experimento.test",
            password = "E2eTest12345!",
            displayName = "Other User"
        })).Content.ReadFromJsonAsync<AuthResponse>(JsonOpts);
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var resp = await other.GetAsync($"/api/formulations/{setup.FormulationId}/next-experiments");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact(DisplayName = "Next experiments endpoint requires authentication")]
    public async Task NextExperiments_Anonymous_Unauthorized()
    {
        var setup = await SeedFormulationAsync(versionsCount: 0);

        var anonymous = _factory.CreateClient();
        var resp = await anonymous.GetAsync($"/api/formulations/{setup.FormulationId}/next-experiments");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    /// <summary>Свежий пользователь с проектом и формуляцией; версии создаются через API из каталога.</summary>
    private async Task<(Guid UserId, Guid FormulationId, List<Guid> VersionIds)> SeedFormulationAsync(int versionsCount)
    {
        var email = $"next_{Guid.NewGuid():N}@experimento.test";
        var auth = await (await _client.PostAsJsonAsync("/api/auth/register",
                new { email, password = "E2eTest12345!", displayName = "Next Experiment User" }))
            .Content.ReadFromJsonAsync<AuthResponse>(JsonOpts);
        Assert.NotNull(auth);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var project = await (await _client.PostAsJsonAsync("/api/projects",
                new { name = "Next experiment project", description = "e2e" }))
            .Content.ReadFromJsonAsync<IdBody>(JsonOpts);
        var formulation = await (await _client.PostAsJsonAsync("/api/formulations",
                new { projectId = project!.Id, name = "Next experiment formulation", targetPurpose = "e2e" }))
            .Content.ReadFromJsonAsync<IdBody>(JsonOpts);

        var versionIds = new List<Guid>();
        for (var i = 0; i < versionsCount; i++)
        {
            var version = await (await _client.PostAsJsonAsync($"/api/formulations/{formulation!.Id}/versions", new
            {
                components = new[]
                {
                    new { chemicalName = "Aspirin", formula = "C9H8O4", molarMass = 180.16,
                          proportion = 1.0, role = "Active", pubChemCid = ApiFixture.AspirinCid }
                },
                conditions = new { temperatureCelsius = 25.0, phTarget = 7.0, solvent = "water" },
                notes = $"seeded v{i + 1}"
            })).Content.ReadFromJsonAsync<IdBody>(JsonOpts);
            versionIds.Add(version!.Id);
        }

        return (auth.User.Id, formulation!.Id, versionIds);
    }

    /// <summary>
    /// История прогонов: у проверенной версии — прогноз с успешным исходом,
    /// у второй — прогноз без исхода и завершённая симуляция с кандидатами.
    /// </summary>
    private void SeedHistory(Guid userId, Guid validatedVersionId, Guid pendingVersionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var model = db.ModelRegistrations.First();

        var validatedJob = new PredictionJob
        {
            VersionId = validatedVersionId, Status = JobStatus.Completed, RequestedBy = userId
        };
        db.PredictionJobs.Add(validatedJob);
        db.PredictionResults.Add(new PredictionResult
        {
            JobId = validatedJob.Id,
            ModelRegistrationId = model.Id,
            SuccessProbability = 0.8,
            ToxicityScore = 0.1,
            StabilityScore = 0.7,
            SideRiskLevel = SideRiskLevel.Low,
            Summary = "seeded outcome",
            Outcome = new ExperimentOutcome
            {
                ActualSuccess = true,
                ActualMetricsJson = "{}",
                RecordedBy = userId
            }
        });

        var pendingJob = new PredictionJob
        {
            VersionId = pendingVersionId, Status = JobStatus.Completed, RequestedBy = userId
        };
        db.PredictionJobs.Add(pendingJob);
        db.PredictionResults.Add(new PredictionResult
        {
            JobId = pendingJob.Id,
            ModelRegistrationId = model.Id,
            SuccessProbability = 0.55,
            ToxicityScore = 0.1,
            StabilityScore = 0.6,
            SideRiskLevel = SideRiskLevel.Low,
            Summary = "seeded prediction without outcome"
        });

        var simulationJob = new SimulationJob
        {
            VersionId = pendingVersionId,
            Status = JobStatus.Completed,
            RequestedBy = userId,
            CompletedAtUtc = DateTime.UtcNow
        };
        db.SimulationJobs.Add(simulationJob);
        db.SimulationResults.Add(new SimulationResult
        {
            JobId = simulationJob.Id,
            IterationsExecuted = 10,
            Summary = "seeded simulation",
            Candidates =
            {
                new SimulationCandidate
                {
                    Rank = 1, Score = 0.9, SuccessProbability = 0.85,
                    ParametersJson = "{\"temperatureCelsius\":38.5,\"phTarget\":6.4}"
                },
                new SimulationCandidate
                {
                    Rank = 2, Score = 0.4, SuccessProbability = 0.35,
                    ParametersJson = "{\"temperatureCelsius\":20}"
                }
            }
        });

        db.SaveChanges();
    }

    private record AuthResponse(UserBody User, string AccessToken);
    private record UserBody(Guid Id, string Email, string DisplayName, string Role);
    private record IdBody(Guid Id);
    private record ParameterBody(string Name, double Value);
    private record RecommendationBody(
        string Kind, string Confidence, string Title, string Rationale, List<string> Evidence,
        Guid? VersionId, int? VersionNumber, double? PredictedSuccessProbability,
        List<ParameterBody> SuggestedParameters);
    private record PlanBody(
        Guid FormulationId, int VersionsTotal, int OutcomesRecorded, double? MeanCalibrationError,
        List<RecommendationBody> Recommendations);
}
