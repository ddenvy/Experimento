using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Experimento.WebApi.Tests;

/// <summary>
/// Отчёт по версии формуляции: эндпоинт уже отдаёт inspection-ready Markdown,
/// тесты фиксируют контракт (контент, тип, имя файла) и авторизацию (403/404).
/// </summary>
public class ReportFlowTests : IClassFixture<ApiFixture>
{
    private readonly HttpClient _client;
    private readonly ApiFixture _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public ReportFlowTests(ApiFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact(DisplayName = "Version report returns inspection-ready Markdown with composition and audit sections")]
    public async Task GetVersionReport_ReturnsMarkdown()
    {
        var setup = await CreateVersionAsync("Report Form", "report content test");

        var resp = await _client.GetAsync($"/api/reports/formulation-versions/{setup.VersionId}/report");
        Assert.True(resp.IsSuccessStatusCode, $"Report failed: {resp.StatusCode}");
        Assert.Equal("text/markdown; charset=utf-8", resp.Content.Headers.ContentType?.ToString());

        var disposition = resp.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition!.DispositionType);
        Assert.EndsWith(".md", disposition.FileName);

        var markdown = await resp.Content.ReadAsStringAsync();
        Assert.StartsWith("# Formulation Report:", markdown);
        Assert.Contains("Report Form v1", markdown);
        Assert.Contains("## Composition", markdown);
        Assert.Contains("## Conditions", markdown);
        Assert.Contains("## Predictions", markdown);
        Assert.Contains("## Simulations", markdown);
        Assert.Contains("## Audit Trail", markdown);
        Assert.Contains("Aspirin", markdown);

        // Несуществующая версия: проверка владения выполняется первой и не раскрывает
        // существование чужого/отсутствующего ресурса — единый контракт 403 для всех эндпоинтов.
        var missing = await _client.GetAsync($"/api/reports/formulation-versions/{Guid.NewGuid()}/report");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, missing.StatusCode);
    }

    [Fact(DisplayName = "Version report is forbidden for a user who does not own the formulation")]
    public async Task GetVersionReport_ForeignUser_Forbidden()
    {
        var setup = await CreateVersionAsync("Owners Form", "isolation test");

        var otherEmail = $"rpother_{Guid.NewGuid():N}@experimento.test";
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email = otherEmail, password = "E2eTest12345!", displayName = "Other User" });
        var otherLogin = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = otherEmail, password = "E2eTest12345!" });
        var otherAuth = await otherLogin.Content.ReadFromJsonAsync<AuthBody>(JsonOpts);
        var otherClient = _factory.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", otherAuth!.AccessToken);

        var foreign = await otherClient.GetAsync(
            $"/api/reports/formulation-versions/{setup.VersionId}/report");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, foreign.StatusCode);
    }

    private async Task<(Guid VersionId, string FormulationName)> CreateVersionAsync(string name, string purpose)
    {
        var email = $"rp_{Guid.NewGuid():N}@experimento.test";
        var regResp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "E2eTest12345!", displayName = "Report User" });
        var auth = await regResp.Content.ReadFromJsonAsync<AuthBody>(JsonOpts);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var project = await PostJson<IdBody>("/api/projects", new { name = "Report Project" });
        var formulation = await PostJson<IdBody>("/api/formulations",
            new { projectId = project.Id, name, targetPurpose = purpose });

        var version = await PostJson<IdBody>($"/api/formulations/{formulation.Id}/versions", new
        {
            formulationId = formulation.Id,
            components = new[]
            {
                new { chemicalName = "Aspirin", molarMass = 180.16, proportion = 1.0, role = "Active",
                      casNumber = "50-78-2", formula = "C9H8O4", pubChemCid = ApiFixture.AspirinCid }
            },
            conditions = new { temperatureCelsius = 25.0, phTarget = 7.0, solvent = "water" }
        });

        return (Guid.Parse(version.Id), name);
    }

    private async Task<TBody> PostJson<TBody>(string url, object body)
    {
        var resp = await _client.PostAsJsonAsync(url, body);
        Assert.True(resp.IsSuccessStatusCode, $"POST {url} failed: {resp.StatusCode}");
        var parsed = await resp.Content.ReadFromJsonAsync<TBody>(JsonOpts);
        Assert.NotNull(parsed);
        return parsed!;
    }

    private record AuthBody(UserBody User, string AccessToken);
    private record UserBody(string Id, string Email, string DisplayName, string Role);
    private record IdBody(string Id);
}
