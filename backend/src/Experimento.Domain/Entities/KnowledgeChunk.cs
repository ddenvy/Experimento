using Pgvector;

namespace Experimento.Domain.Entities;

/// <summary>
/// A chunked piece of a knowledge document with its vector embedding.
/// </summary>
public class KnowledgeChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public KnowledgeDocument Document { get; set; } = null!;
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public Vector Embedding { get; set; } = new(Array.Empty<float>());
    public string MetadataJson { get; set; } = "{}";
}
