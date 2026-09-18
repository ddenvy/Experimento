using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;

namespace Experimento.Ai;

/// <summary>
/// Text embedding service wrapping Semantic Kernel's embedding generation.
/// Falls back to a deterministic offline pseudo-embedding when no provider is configured.
/// </summary>
public class EmbeddingService : IEmbeddingService
{
    private readonly ITextEmbeddingGenerationService? _embedding;
    private readonly string _provider;
    private readonly ILogger<EmbeddingService>? _logger;
    private int _fallbackWarningLogged;
    private const int EmbeddingDimensions = 1536;
    public int Dimensions => EmbeddingDimensions;

    public EmbeddingService(ITextEmbeddingGenerationService? embedding, string provider,
        ILogger<EmbeddingService>? logger = null)
    {
        _embedding = embedding;
        _provider = provider;
        _logger = logger;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var batch = await EmbedBatchAsync([text], cancellationToken);
        return batch[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
            return Array.Empty<float[]>();

        if (_embedding is null || _provider == "none")
        {
            if (Interlocked.Exchange(ref _fallbackWarningLogged, 1) == 0)
            {
                _logger?.LogWarning(
                    "Embedding provider '{Provider}' is not configured — using deterministic offline pseudo-embeddings. " +
                    "Semantic search quality will be degraded; configure a real embedding provider for production.",
                    _provider);
            }
            return texts.Select(DeterministicFallback).ToList();
        }

        var embeddings = await _embedding.GenerateEmbeddingsAsync(texts.ToList(), cancellationToken: cancellationToken);
        var result = new float[texts.Count][];
        for (int i = 0; i < texts.Count; i++)
        {
            // Пустой вектор от РЕАЛЬНОГО провайдера — это сбой, а не повод для fallback:
            // fallback-векторы лежат в другом пространстве и тихо ломают семантический поиск.
            var vector = embeddings[i];
            if (vector.IsEmpty)
                throw new InvalidOperationException(
                    $"Embedding provider '{_provider}' returned an empty vector for input #{i}.");
            result[i] = vector.ToArray();
        }
        return result;
    }

    /// <summary>
    /// Стабильный детерминированный офлайн-эмбеддинг.
    /// ВАЖНО: нельзя использовать string.GetHashCode() — он рандомизируется для каждого процесса,
    /// из-за чего ранее сохранённые векторы перестали бы совпадать после рестарта.
    /// Здесь применяется чисто-функциональная цепочка SHA-256, не зависящая от процесса/машины.
    /// </summary>
    private static float[] DeterministicFallback(string text)
    {
        var vec = new float[EmbeddingDimensions];
        var prefix = System.Text.Encoding.UTF8.GetBytes("experimento-embedding-v1|");
        var textBytes = System.Text.Encoding.UTF8.GetBytes(text);
        var seedMaterial = new byte[prefix.Length + textBytes.Length];
        prefix.CopyTo(seedMaterial, 0);
        textBytes.CopyTo(seedMaterial, prefix.Length);

        var block = SHA256.HashData(seedMaterial);
        int filled = 0;
        int counter = 0;
        while (filled < vec.Length)
        {
            foreach (var b in block)
            {
                if (filled >= vec.Length) break;
                vec[filled++] = (b / 255f) * 2f - 1f;
            }
            counter++;
            var chained = new byte[block.Length + sizeof(int)];
            block.CopyTo(chained, 0);
            BitConverter.TryWriteBytes(chained.AsSpan(block.Length), counter);
            block = SHA256.HashData(chained);
        }

        // Нормализация до единичной длины для корректного косинусного расстояния.
        var norm = (float)Math.Sqrt(vec.Sum(v => v * v));
        if (norm > 0)
            for (int i = 0; i < vec.Length; i++)
                vec[i] /= norm;
        return vec;
    }
}
