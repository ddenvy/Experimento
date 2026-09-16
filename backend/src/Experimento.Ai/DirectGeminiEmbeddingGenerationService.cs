using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;

namespace Experimento.Ai;

/// <summary>
/// Прямой REST-клиент эмбеддингов Gemini.
/// Использует API версии v1 (в v1beta модели gemini-embedding-* недоступны) и эндпоинт
/// batchEmbedContents. outputDimensionality=1536 задан под колонку pgvector vector(1536).
/// </summary>
public sealed class DirectGeminiEmbeddingGenerationService : ITextEmbeddingGenerationService
{
    private const int Dimensions = 1536;
    // Лимит запросов в одном batchEmbedContents вызове — 100.
    private const int BatchSize = 100;

    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _apiKey;

    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public DirectGeminiEmbeddingGenerationService(HttpClient http, string model, string apiKey)
    {
        _http = http;
        _model = model;
        _apiKey = apiKey;
    }

    public async Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IList<string> data,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        if (data.Count == 0)
            return Array.Empty<ReadOnlyMemory<float>>();

        var result = new ReadOnlyMemory<float>[data.Count];

        for (var offset = 0; offset < data.Count; offset += BatchSize)
        {
            var count = Math.Min(BatchSize, data.Count - offset);
            var requests = new object[count];
            for (var i = 0; i < count; i++)
            {
                requests[i] = new
                {
                    model = $"models/{_model}",
                    outputDimensionality = Dimensions,
                    content = new { parts = new[] { new { text = data[offset + i] } } }
                };
            }

            var url = $"https://generativelanguage.googleapis.com/v1/models/{_model}:batchEmbedContents";
            using var response = await SendWithRetryAsync(url, new { requests }, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Gemini embeddings API returned {(int)response.StatusCode}: {Truncate(raw)}");

            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("embeddings", out var embeddings) ||
                embeddings.GetArrayLength() != count)
            {
                throw new HttpRequestException("Gemini embeddings API returned an unexpected payload.");
            }

            for (var i = 0; i < count; i++)
            {
                var values = embeddings[i].GetProperty("values");
                var vector = new float[values.GetArrayLength()];
                for (var j = 0; j < vector.Length; j++)
                    vector[j] = (float)values[j].GetDouble();
                result[offset + i] = vector;
            }
        }

        return result;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(string url, object body, CancellationToken ct)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body)
            };
            // Ключ в заголовке, а не в query: URL с ?key= оседает в access-логах прокси.
            request.Headers.TryAddWithoutValidation("x-goog-api-key", _apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return response;
            var status = (int)response.StatusCode;
            // Транзиентные: 429 (rate limit) и 5xx.
            var retryable = status == 429 || (status >= 500 && status <= 504);
            if (!retryable || attempt == maxAttempts) return response;
            response.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
        }
        throw new InvalidOperationException("Unreachable");
    }

    private static string Truncate(string text)
        => text.Length <= 500 ? text : text[..500] + "…";
}
