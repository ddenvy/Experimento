using Experimento.Domain.Enums;

namespace Experimento.Domain.Entities;

/// <summary>
/// A document in the knowledge base (patents, papers, internal experiments).
/// </summary>
public class KnowledgeDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Title { get; set; } = string.Empty;
    public SourceType SourceType { get; set; }
    public string Reference { get; set; } = string.Empty;
    public KnowledgeStatus Status { get; set; } = KnowledgeStatus.Pending;
    public Guid UploadedBy { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<KnowledgeChunk> Chunks { get; set; } = new List<KnowledgeChunk>();
}
