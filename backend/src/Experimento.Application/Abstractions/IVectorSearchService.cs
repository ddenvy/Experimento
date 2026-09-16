using Experimento.Domain.Entities;

namespace Experimento.Application.Abstractions;

/// <summary>
/// Семантический векторный поиск по чанкам знаний.
/// </summary>
public interface IVectorSearchService
{
    /// <summary>
    /// Поиск с учётом видимости: глобальные документы (ProjectId == null),
    /// собственные загрузки и документы проектов, принадлежащих пользователю.
    /// </summary>
    /// <param name="userId">Идентификатор пользователя. <see cref="Guid.Empty"/> — системный вызов,
    /// которому доступны только глобальные документы.</param>
    Task<IReadOnlyList<(KnowledgeChunk Chunk, KnowledgeDocument Document, double Similarity)>> SearchAsync(
        string query, int topK, Guid? projectId, Guid userId, CancellationToken cancellationToken = default);
}
