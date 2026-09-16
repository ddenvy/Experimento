using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Experimento.WebApi.Tests;

/// <summary>
/// End-to-end test of the full Experimento workflow through the HTTP API:
/// register → login → project → formulation → version → prediction → audit.
/// Uses the in-process test server (ApiFixture) backed by the host Postgres + RabbitMQ.
/// </summary>
public class FullFlowTests : IClassFixture<ApiFixture>
{
    private readonly HttpClient _client;
    private readonly ApiFixture _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public FullFlowTests(ApiFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact(DisplayName = "Full flow: register → login → project → formulation → version → prediction → audit")]
    public async Task FullWorkflow_CompletesSuccessfully()
    {
        // --- 1. Register a fresh user ---
        var email = $"e2e_{Guid.NewGuid():N}@experimento.test";
        var registerBody = new { email, password = "E2eTest12345!", displayName = "E2E User" };
        var regResp = await _client.PostAsJsonAsync("/api/auth/register", registerBody);
        Assert.True(regResp.IsSuccessStatusCode, $"Register failed: {regResp.StatusCode}");
        var reg = await regResp.Content.ReadFromJsonAsync<AuthResponse>(JsonOpts);
        Assert.NotNull(reg);
        Assert.False(string.IsNullOrEmpty(reg.AccessToken));
        var userId = reg.User.Id;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reg.AccessToken);

        // --- 2. Login with the same credentials ---
        var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "E2eTest12345!" });
        Assert.True(loginResp.IsSuccessStatusCode, $"Login failed: {loginResp.StatusCode}");
        var login = await loginResp.Content.ReadFromJsonAsync<AuthResponse>(JsonOpts);
        Assert.NotNull(login);
        Assert.Equal(email, login.User.Email);

        // --- 3. Create a project ---
        var projectResp = await _client.PostAsJsonAsync("/api/projects",
            new { name = "E2E Project", description = "Created by integration test" });
        Assert.True(projectResp.IsSuccessStatusCode, $"Create project failed: {projectResp.StatusCode}");
        var project = await projectResp.Content.ReadFromJsonAsync<ProjectDto>(JsonOpts);
        Assert.NotNull(project);
        Assert.Equal("E2E Project", project.Name);
        var projectId = project.Id;

        // --- 4. Create a formulation ---
        var formResp = await _client.PostAsJsonAsync("/api/formulations",
            new { projectId, name = "E2E Formulation", targetPurpose = "Test solubility" });
        Assert.True(formResp.IsSuccessStatusCode, $"Create formulation failed: {formResp.StatusCode}");
        var formulation = await formResp.Content.ReadFromJsonAsync<FormulationDto>(JsonOpts);
        Assert.NotNull(formulation);
        var formulationId = formulation.Id;

        // --- 5. Create a formulation version with catalog-verified components and conditions ---
        var versionBody = new
        {
            formulationId,
            components = new[]
            {
                new { chemicalName = "Aspirin", casNumber = "50-78-2", formula = "C9H8O4",
                      molarMass = 180.16, proportion = 0.6, role = "Active",
                      pubChemCid = ApiFixture.AspirinCid },
                new { chemicalName = "Sodium chloride", casNumber = "7647-14-5", formula = "ClNa",
                      molarMass = 58.44, proportion = 0.4, role = "Excipient",
                      pubChemCid = ApiFixture.SodiumChlorideCid }
            },
            conditions = new { temperatureCelsius = 25.0, phTarget = 7.0, solvent = "water" },
            notes = "E2E test version"
        };
        var verResp = await _client.PostAsJsonAsync($"/api/formulations/{formulationId}/versions", versionBody);
        Assert.True(verResp.IsSuccessStatusCode, $"Create version failed: {verResp.StatusCode}");
        var version = await verResp.Content.ReadFromJsonAsync<VersionDto>(JsonOpts);
        Assert.NotNull(version);
        var versionId = version.Id;

        // --- 5b. A component with an unknown PubChem CID must be rejected ---
        var bogusBody = new
        {
            formulationId,
            components = new[]
            {
                new { chemicalName = "Unobtainium", casNumber = (string?)null, formula = (string?)null,
                      molarMass = 999.0, proportion = 1.0, role = "Active", pubChemCid = 2_000_000_000 }
            },
            conditions = new { temperatureCelsius = 25.0 }
        };
        var bogusResp = await _client.PostAsJsonAsync($"/api/formulations/{formulationId}/versions", bogusBody);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, bogusResp.StatusCode);

        // --- 6. Submit a prediction job ---
        var predResp = await _client.PostAsync($"/api/predictions/formulation-versions/{versionId}/predictions", null);
        Assert.True(predResp.IsSuccessStatusCode, $"Submit prediction failed: {predResp.StatusCode}");
        var job = await predResp.Content.ReadFromJsonAsync<JobDto>(JsonOpts);
        Assert.NotNull(job);
        var jobId = job.Id;

        // --- 7. Poll for the prediction result (consumer runs in-process via MassTransit) ---
        PredictionResultDto? result = null;
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(1000);
            var resResp = await _client.GetAsync($"/api/predictions/prediction-jobs/{jobId}/result");
            if (resResp.IsSuccessStatusCode)
            {
                result = await resResp.Content.ReadFromJsonAsync<PredictionResultDto>(JsonOpts);
                if (result != null) break;
            }
        }
        Assert.NotNull(result);
        Assert.InRange(result.SuccessProbability, 0.0, 1.0);
        Assert.False(string.IsNullOrEmpty(result.SideRiskLevel));

        // --- 7b. Run history for the version contains the prediction run ---
        // Результат сохраняется до финального статуса job, поэтому дожидаемся терминального
        // статуса; конкретный Completed не проверяем — внешний эмбеддинг-провайдер может
        // транзиторно падать на стадии rationale (это не связано с эндпоинтом истории).
        PredictionRunSummary? run = null;
        for (var i = 0; i < 10; i++)
        {
            var historyResp = await _client.GetAsync($"/api/predictions/formulation-versions/{versionId}/predictions");
            Assert.True(historyResp.IsSuccessStatusCode, $"Prediction history failed: {historyResp.StatusCode}");
            var historyNow = await historyResp.Content.ReadFromJsonAsync<List<PredictionRunSummary>>(JsonOpts);
            Assert.NotNull(historyNow);
            run = historyNow.SingleOrDefault(r => r.JobId == jobId);
            if (run is not null && (run.Status == "Completed" || run.Status == "Failed")) break;
            await Task.Delay(1000);
        }
        Assert.NotNull(run);
        Assert.NotNull(run.ResultId);

        // Simulation history endpoint works even when no simulations were run.
        var simHistoryResp = await _client.GetAsync($"/api/simulations/formulation-versions/{versionId}/simulations");
        Assert.True(simHistoryResp.IsSuccessStatusCode, $"Simulation history failed: {simHistoryResp.StatusCode}");
        var simHistory = await simHistoryResp.Content.ReadFromJsonAsync<List<SimulationRunSummary>>(JsonOpts);
        Assert.NotNull(simHistory);
        Assert.Empty(simHistory);

        // History of a foreign/unknown version is forbidden, not leaked.
        var foreignResp = await _client.GetAsync($"/api/predictions/formulation-versions/{Guid.NewGuid()}/predictions");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, foreignResp.StatusCode);

        // --- 8. Audit trail is Admin-only: log in as the seeded admin and verify entries ---
        var adminResp = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "apple@apple.com", password = "Test12345!" });
        Assert.True(adminResp.IsSuccessStatusCode, $"Admin login failed: {adminResp.StatusCode}");
        var admin = await adminResp.Content.ReadFromJsonAsync<AuthResponse>(JsonOpts);
        Assert.NotNull(admin);
        Assert.Equal("Admin", admin.User.Role);

        var auditClient = _factory.CreateClient();
        auditClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var auditResp = await auditClient.GetAsync($"/api/audit?entityType=Formulation&entityId={formulationId}");
        Assert.True(auditResp.IsSuccessStatusCode, $"Audit trail failed: {auditResp.StatusCode}");
        var audit = await auditResp.Content.ReadFromJsonAsync<List<AuditEntry>>(JsonOpts);
        Assert.NotNull(audit);
        Assert.Contains(audit, a => a.Action == "Formulation.Create");
    }

    [Fact(DisplayName = "Login with wrong password returns 401 with clean error")]
    public async Task Login_WrongPassword_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@experimento.test", password = "wrong" });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorBody>(JsonOpts);
        Assert.NotNull(body);
        Assert.Equal("Invalid credentials.", body.Error);
    }

    [Fact(DisplayName = "Protected endpoint without token returns 401")]
    public async Task ProtectedEndpoint_NoToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var resp = await _client.GetAsync("/api/projects");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // --- DTOs for deserialization ---
    private record AuthResponse(UserDto User, string AccessToken);
    private record UserDto(string Id, string Email, string DisplayName, string Role);
    private record ProjectDto(string Id, string Name, string Description, DateTime CreatedAtUtc);
    private record FormulationDto(string Id, string Name, string TargetPurpose, int CurrentVersionNumber);
    private record VersionDto(string Id, int VersionNumber);
    private record JobDto(string Id, string Status);
    private record PredictionResultDto(string Id, string JobId, double SuccessProbability, double ToxicityScore,
        double StabilityScore, string SideRiskLevel, string Summary, List<RationaleItemDto> RationaleItems);
    private record RationaleItemDto(string Id, string Category, string Claim, string Explanation, double Confidence,
        List<RationaleSourceDto> Sources);
    private record RationaleSourceDto(string Title, string Reference, string Type, double Similarity);
    private record PredictionRunSummary(string JobId, string? ResultId, string Status);
    private record SimulationRunSummary(string JobId);
    private record AuditEntry(string Action, string EntityType, string EntityId);
    private record ErrorBody(string Error);
}
