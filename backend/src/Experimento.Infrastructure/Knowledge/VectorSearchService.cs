using Microsoft.EntityFrameworkCore;
using Experimento.Infrastructure.Data;
using Pgvector.EntityFrameworkCore;

namespace Experimento.Infrastructure.Knowledge;

/// <summary>
/// Векторный поиск по чанкам знаний (pgvector, косинусное расстояние).
/// Фильтрация области видимости выполняется в SQL ДО сортировки и Take(topK).
/// </summary>
public class VectorSearchService : IVectorSearchService
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embedding;
    public VectorSearchService(AppDbContext db, IEmbeddingService embedding) => (_db, _embedding) = (db, embedding);

    public async Task<IReadOnlyList<(KnowledgeChunk Chunk, KnowledgeDocument Document, double Similarity)>> SearchAsync(
        string query, int topK, Guid? projectId, Guid userId, CancellationToken cancellationToken = default)
    {
        var queryEmbedding = new Pgvector.Vector(await _embedding.EmbedAsync(query, cancellationToken));

        var search = _db.KnowledgeChunks
            .Include(c => c.Document)
            .Where(c => c.Document != null);

        if (projectId is Guid pid)
        {
            // Поиск в конкретном проекте — только по его чанкам.
            search = search.Where(c => c.Document!.ProjectId == pid);
        }
        else if (userId == Guid.Empty)
        {
            // Системный контекст (генерация rationale): только глобальная база.
            search = search.Where(c => c.Document!.ProjectId == null);
        }
        else
        {
            // Пользовательский контекст: глобальные + собственные + документы своих проектов.
            search = search.Where(c =>
                c.Document!.ProjectId == null ||
                c.Document.UploadedBy == userId ||
                _db.Projects.Any(p => p.Id == c.Document.ProjectId && p.CreatedBy == userId));
        }

        var raw = await search
            .OrderBy(c => c.Embedding.CosineDistance(queryEmbedding))
            .Take(topK)
            .Select(c => new { Chunk = c, c.Document })
            .ToListAsync(cancellationToken);

        var results = new List<(KnowledgeChunk Chunk, KnowledgeDocument Document, double Similarity)>(raw.Count);
        foreach (var item in raw)
        {
            // Косинусное расстояние в [0, 2]; сходство = 1 - расстояние/2.
            var distance = CosineDistance(item.Chunk.Embedding, queryEmbedding);
            var similarity = 1.0 - distance / 2.0;
            results.Add((item.Chunk, item.Document!, similarity));
        }

        return results;
    }

    private static double CosineDistance(Pgvector.Vector a, Pgvector.Vector b)
    {
        var va = a.ToArray();
        var vb = b.ToArray();
        double dot = 0, normA = 0, normB = 0;
        int len = Math.Min(va.Length, vb.Length);
        for (int i = 0; i < len; i++)
        {
            dot += va[i] * vb[i];
            normA += va[i] * va[i];
            normB += vb[i] * vb[i];
        }
        if (normA == 0 || normB == 0) return 2;
        return 1.0 - dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
