using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Experimento.Ai;

/// <summary>
/// Direct Gemini REST API client (avoids the Semantic Kernel Google connector
/// which has URL-construction issues with current model IDs).
/// Calls https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent.
/// </summary>
public class DirectGeminiChatCompletionService : IChatCompletionService
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _apiKey;

    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public DirectGeminiChatCompletionService(HttpClient http, string model, string apiKey)
    {
        _http = http;
        _model = model;
        _apiKey = apiKey;
    }

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        // Ключ передаётся заголовком, а не query string: URL с ?key= оседает в access-логах прокси.
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent";

        var contents = new List<object>();
        foreach (var msg in chatHistory)
        {
            var role = msg.Role == AuthorRole.System ? "user" : msg.Role.Label;
            contents.Add(new
            {
                role = role,
                parts = new[] { new { text = msg.Content } }
            });
        }

        var body = new
        {
            contents,
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 1024
            }
        };

        using var response = await SendWithRetryAsync(url, body, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Gemini API returned {(int)response.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        // Safety-блок или пустой ответ: кандидатов может не быть — не падаем, отдаём пустой список,
        // тогда обогащение rationale просто пропускается.
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return Array.Empty<ChatMessageContent>();

        var first = candidates[0];
        if (!first.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.GetArrayLength() == 0)
        {
            return Array.Empty<ChatMessageContent>();
        }

        var text = parts[0].TryGetProperty("text", out var textEl)
            ? textEl.GetString() ?? string.Empty
            : string.Empty;

        var message = new ChatMessageContent(AuthorRole.Assistant, text);
        return new[] { message };
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var result = await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
        foreach (var msg in result)
        {
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, msg.Content);
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(string url, object body, CancellationToken ct)
    {
        const int maxAttempts = 4;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.TryAddWithoutValidation("x-goog-api-key", _apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return response;
            var status = (int)response.StatusCode;
            // Ретраим транзиентные ошибки: 429 (rate limit), 500, 502, 503, 504.
            bool retryable = status == 429 || (status >= 500 && status <= 504);
            if (!retryable || attempt == maxAttempts) return response;
            response.Dispose();
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            await Task.Delay(delay, ct);
        }
        throw new InvalidOperationException("Unreachable");
    }
}
