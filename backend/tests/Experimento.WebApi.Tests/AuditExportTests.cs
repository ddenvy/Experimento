using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Experimento.WebApi.Security;
using Xunit;

namespace Experimento.WebApi.Tests;

/// <summary>
/// Пакет аудита: один Markdown-документ с оценкой ALCOA+, полным журналом,
/// инвентарём версий, прогонами и ревью. Тесты фиксируют контракт выгрузки
/// (тип, вложение, разделы, само-проверяемый отпечаток) и разграничение доступа.
/// </summary>
public class AuditExportTests : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly ApiFixture _factory;
    private readonly HttpClient _admin;

    public AuditExportTests(ApiFixture factory)
    {
        _factory = factory;
        _admin = factory.CreateClient();
        AuthenticateAsync(_admin, DbSeeder.TestAdminEmail, DbSeeder.TestAdminPassword).GetAwaiter().GetResult();
    }

    [Fact(DisplayName = "Audit export returns a Markdown attachment with all ALCOA+ sections")]
    public async Task Export_AsAdmin_ReturnsMarkdownPackage()
    {
        var result = await ExportAsync(_admin);

        Assert.Equal("text/markdown; charset=utf-8", result.Response.Content.Headers.ContentType?.ToString());

        var disposition = result.Response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition!.DispositionType);
        Assert.StartsWith("audit-export-", disposition.FileName);
        Assert.EndsWith(".md", disposition.FileName);

        Assert.StartsWith("# Audit Export Package", result.Markdown);
        Assert.Contains("## ALCOA+ Compliance Assessment", result.Markdown);
        Assert.Contains("## Formulation Versions Inventory", result.Markdown);
        Assert.Contains("## Prediction Runs", result.Markdown);
        Assert.Contains("## Simulation Runs", result.Markdown);
        Assert.Contains("## Reviews and Signatures", result.Markdown);
        Assert.Contains("## Full Audit Trail", result.Markdown);
        Assert.Contains("## Integrity Statement", result.Markdown);
        Assert.EndsWith("*End of audit export package.*" + Environment.NewLine, result.Markdown);

        // Все принципы ALCOA+ должны присутствовать в таблице оценки.
        foreach (var principle in new[]
                 {
                     "Attributable", "Legible", "Contemporaneous", "Original", "Accurate",
                     "Complete", "Consistent", "Enduring", "Available"
                 })
        {
            Assert.Contains($"| {principle} |", result.Markdown);
        }

        Assert.Matches(@"\*\*Overall: (COMPLIANT|COMPLIANT WITH WARNINGS|NOT COMPLIANT)\*\*", result.Markdown);
    }

    [Fact(DisplayName = "Audit export document fingerprint matches its own body")]
    public async Task Export_Fingerprint_IsSelfConsistent()
    {
        var result = await ExportAsync(_admin);

        const string marker = "**Document fingerprint (SHA-256):**";
        var markerIndex = result.Markdown.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex > 0, "Fingerprint line is missing from the export.");

        // Отпечаток считается по всему телу документа выше строки с отпечатком.
        var body = result.Markdown[..markerIndex];
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        var match = Regex.Match(result.Markdown[(markerIndex + marker.Length)..], "[0-9a-f]{64}");
        Assert.True(match.Success, "Fingerprint value is missing or malformed.");
        Assert.Equal(expected, match.Value);
    }

    [Fact(DisplayName = "Audit export records its own download in the journal")]
    public async Task Export_IsRecordedInJournal()
    {
        await ExportAsync(_admin);

        var trail = await _admin.GetFromJsonAsync<List<AuditEntryBody>>("/api/audit?take=20", JsonOpts);
        Assert.NotNull(trail);
        Assert.Contains(trail!, e => e.Action == "Audit.Export" && e.EntityType == "AuditTrail");
    }

    [Fact(DisplayName = "Audit export is forbidden for a non-admin user")]
    public async Task Export_AsScientist_Forbidden()
    {
        var scientist = _factory.CreateClient();
        var email = $"sci_{Guid.NewGuid():N}@experimento.test";
        await scientist.PostAsJsonAsync("/api/auth/register",
            new { email, password = "E2eTest12345!", displayName = "Scientist User" });
        await AuthenticateAsync(scientist, email, "E2eTest12345!");

        var resp = await scientist.GetAsync("/api/audit/export");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact(DisplayName = "Audit export requires authentication")]
    public async Task Export_Anonymous_Unauthorized()
    {
        var anonymous = _factory.CreateClient();
        var resp = await anonymous.GetAsync("/api/audit/export");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    private static async Task<(HttpResponseMessage Response, string Markdown)> ExportAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/audit/export");
        Assert.True(response.IsSuccessStatusCode, $"Export failed: {response.StatusCode}");
        return (response, await response.Content.ReadAsStringAsync());
    }

    private static async Task AuthenticateAsync(HttpClient client, string email, string password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var auth = await login.Content.ReadFromJsonAsync<AuthBody>(JsonOpts);
        Assert.NotNull(auth);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
    }

    private record AuthBody(UserBody User, string AccessToken);
    private record UserBody(string Id, string Email, string DisplayName, string Role);
    private record AuditEntryBody(long Id, DateTime TimestampUtc, Guid? ActorUserId, string Action, string EntityType, string? EntityId);
}
