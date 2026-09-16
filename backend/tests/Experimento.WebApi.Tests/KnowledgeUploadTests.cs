using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Experimento.WebApi.Tests;

/// <summary>
/// Интеграционный тест загрузки файла в Knowledge Base:
/// multipart CSV → извлечение таблицы → индексация воркером → семантический поиск.
/// </summary>
public class KnowledgeUploadTests : IClassFixture<ApiFixture>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // Минимальный срез ответа авторизации (полные DTO живут внутри FullFlowTests).
    private sealed record LoginResponse(AuthUser User, string AccessToken);
    private sealed record AuthUser(string Id);

    public KnowledgeUploadTests(ApiFixture factory) => _client = factory.CreateClient();

    [Fact(DisplayName = "Knowledge: CSV upload is parsed, indexed and found by semantic search")]
    public async Task CsvFileUpload_IsIndexedAndSearchable()
    {
        var email = $"kb_{Guid.NewGuid():N}@experimento.test";
        var regResp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "E2eTest12345!", displayName = "KB User" });
        Assert.Equal(HttpStatusCode.OK, regResp.StatusCode);
        var reg = await regResp.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);
        Assert.NotNull(reg);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reg!.AccessToken);

        var csv = "compound,application,solubility\r\n" +
                  "Caffeine,central nervous system stimulant in coffee,20 mg/mL\r\n" +
                  "Aspirin,pain reliever and antiplatelet drug,3 mg/mL\r\n";
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "lab-data.csv" },
            { new StringContent("Paper"), "sourceType" }
        };

        var uploadResp = await _client.PostAsync("/api/knowledge/documents/upload", form);
        Assert.True(uploadResp.IsSuccessStatusCode,
            $"Upload failed: {uploadResp.StatusCode} {await uploadResp.Content.ReadAsStringAsync()}");
        var doc = await uploadResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var docId = doc.GetProperty("id").GetGuid();
        Assert.Equal("lab-data", doc.GetProperty("title").GetString());

        // Воркер чанкует и строит эмбеддинги — ждём Ready.
        var ready = false;
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(1000);
            var docs = await _client.GetFromJsonAsync<JsonElement[]>("/api/knowledge/documents", JsonOpts);
            Assert.NotNull(docs);
            var status = docs!.FirstOrDefault(d => d.GetProperty("id").GetGuid() == docId)
                .GetProperty("status").GetString();
            if (status == "Ready") { ready = true; break; }
            Assert.NotEqual("Failed", status);
        }
        Assert.True(ready, "Document did not reach Ready status in time.");

        // Документы без projectId попадают в глобальную базу и видны всем пользователям,
        // поэтому берём максимальный topK и проверяем наличие именно нашего разобранного чанка.
        var searchResp = await _client.PostAsJsonAsync("/api/knowledge/search",
            new { query = "caffeine coffee stimulant", topK = 50 });
        Assert.True(searchResp.IsSuccessStatusCode, searchResp.StatusCode.ToString());
        var results = await searchResp.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        Assert.NotNull(results);
        Assert.Contains(results!, r =>
            r.GetProperty("documentTitle").GetString() == "lab-data" &&
            r.GetProperty("content").GetString()?.Contains("compound: Caffeine") == true);
    }

    [Fact(DisplayName = "Knowledge: unsupported file type is rejected with 400")]
    public async Task UnsupportedFileType_Returns400()
    {
        var email = $"kb2_{Guid.NewGuid():N}@experimento.test";
        var regResp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "E2eTest12345!", displayName = "KB User 2" });
        var reg = await regResp.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reg!.AccessToken);

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "file", "malware.exe" }
        };
        var resp = await _client.PostAsync("/api/knowledge/documents/upload", form);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
