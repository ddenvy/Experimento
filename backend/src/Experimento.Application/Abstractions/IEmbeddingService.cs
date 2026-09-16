namespace Experimento.Application.Abstractions;

/// <summary>
/// Text embedding service for vector search.
/// </summary>
public interface IEmbeddingService
{
    int Dimensions { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Пакетное построение эмбеддингов одним вызовом провайдера (эффективнее, чем по одному).
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}
