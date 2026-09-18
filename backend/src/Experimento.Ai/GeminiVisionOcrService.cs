using System.Net.Http.Json;
using System.Text.Json;
using Experimento.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Experimento.Ai;

/// <summary>
/// Распознавание страниц лабораторного журнала через мультимодальный Gemini:
/// изображение передаётся как inline_data вместе с инструкцией транскрипции.
/// Модель обязана помечать неразборчивые слова и не додумывать данные —
/// это первичные лабораторные записи, а не свободный текст.
/// </summary>
public class GeminiVisionOcrService : ILabNoteOcr
{
    /// <summary>Маркер ответа, когда на изображении нет читаемого лабораторного содержимого.</summary>
    private const string NoContentMarker = "NO_CONTENT";

    private const string TranscriptionPrompt = """
        You are transcribing a page from a laboratory notebook. The page is provided as an image.

        Step 1 - transcription:
        - Transcribe ALL legible text exactly as written, preserving the original language and wording. Do not translate.
        - Preserve the reading order of the page.
        - Mark any word you cannot read confidently as [?]. Never guess a substance name, an amount or a number.
        - Do not invent, complete, correct or summarise away any data.

        Step 2 - normalised summary:
        Append labelled lines, but only for information actually present on the page:
        COMPOSITION: substance names with amounts, masses or proportions
        CONDITIONS: temperature, pH, solvent, time, equipment
        OBSERVATIONS: what was observed (colour, precipitate, phase separation, odour, temperature change)
        ACTIONS: what was done, step by step

        If the image contains no legible laboratory content, reply with exactly: NO_CONTENT
        """;

    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _apiKey;
    private readonly ILogger<GeminiVisionOcrService>? _logger;

    public GeminiVisionOcrService(HttpClient http, string model, string apiKey,
        ILogger<GeminiVisionOcrService>? logger = null)
    {
        _http = http;
        _model = model;
        _apiKey = apiKey;
        _logger = logger;
    }

    public async Task<string> TranscribeAsync(byte[] image, string contentType,
        CancellationToken cancellationToken = default)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent";

        var body = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = TranscriptionPrompt },
                        new
                        {
                            inline_data = new
                            {
                                mime_type = contentType,
                                data = Convert.ToBase64String(image)
                            }
                        }
                    }
                }
            },
            // Нулевая температура: транскрипция должна быть воспроизводимой, без «творчества».
            generationConfig = new { temperature = 0.0, maxOutputTokens = 8192 }
        };

        using var response = await SendWithRetryAsync(url, body, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini vision API returned {(int)response.StatusCode}: {raw}");

        using var doc = JsonDocument.Parse(raw);
        var text = ReadFirstTextPart(doc.RootElement).Trim();

        if (text.Length == 0)
        {
            _logger?.LogWarning("Gemini vision returned no candidates for a {Bytes}-byte {ContentType} image", image.Length, contentType);
            return string.Empty;
        }

        // Safety-блок или страница без читаемого содержимого — сигнализируем вызывающему коду.
        if (text.Equals(NoContentMarker, StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (text.StartsWith(NoContentMarker, StringComparison.OrdinalIgnoreCase))
            text = text[NoContentMarker.Length..].Trim();

        return text;
    }

    /// <summary>Текст первого part первого кандидата; пусто, если модель заблокирована safety-фильтром.</summary>
    private static string ReadFirstTextPart(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return string.Empty;
        if (!candidates[0].TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0)
            return string.Empty;
        return parts[0].TryGetProperty("text", out var text) ? text.GetString() ?? string.Empty : string.Empty;
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
            // Ключ передаётся заголовком, а не query string: URL с ?key= оседает в access-логах прокси.
            request.Headers.TryAddWithoutValidation("x-goog-api-key", _apiKey);
            request.Headers.Add("Accept", "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                // Обрыв соединения на транспортном уровне (не HTTP-статус) — тоже транзиентная ошибка.
                _logger?.LogWarning(ex, "Gemini vision request attempt {Attempt} failed, retrying", attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                continue;
            }

            if (response.IsSuccessStatusCode) return response;

            var status = (int)response.StatusCode;
            // Ретраим транзиентные ошибки: 429 (rate limit), 500, 502, 503, 504.
            var retryable = status == 429 || status is >= 500 and <= 504;
            if (!retryable || attempt == maxAttempts) return response;

            response.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
        }
        throw new InvalidOperationException("Unreachable");
    }
}
