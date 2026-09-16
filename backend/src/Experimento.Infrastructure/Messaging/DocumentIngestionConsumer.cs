using MassTransit;
using Microsoft.Extensions.Logging;
using Experimento.Infrastructure.Data;
using Experimento.Infrastructure.Knowledge;

namespace Experimento.Infrastructure.Messaging;

/// <summary>
/// Consumes document ingestion commands: chunks content and embeds each chunk.
/// </summary>
public class DocumentIngestionConsumer : IConsumer<IngestDocumentCommand>
{
    private readonly AppDbContext _db;
    private readonly ChunkingService _chunking;
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<DocumentIngestionConsumer> _logger;

    public DocumentIngestionConsumer(AppDbContext db, ChunkingService chunking, IEmbeddingService embedding,
        ILogger<DocumentIngestionConsumer> logger)
    {
        _db = db;
        _chunking = chunking;
        _embedding = embedding;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IngestDocumentCommand> context)
    {
        var docId = context.Message.DocumentId;
        var doc = await _db.KnowledgeDocuments.FindAsync([docId]);
        if (doc is null) return;

        try
        {
            doc.Status = "Processing";
            await _db.SaveChangesAsync();

            var content = context.Message.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                doc.Status = "Failed";
                await _db.SaveChangesAsync();
                return;
            }

            var chunks = _chunking.Chunk(content);

            // Эмбеддинги строятся одним пакетом — один сетевой вызов вместо N.
            var vectors = await _embedding.EmbedBatchAsync(chunks, context.CancellationToken);
            for (int i = 0; i < chunks.Count; i++)
            {
                _db.KnowledgeChunks.Add(new KnowledgeChunk
                {
                    DocumentId = docId,
                    ChunkIndex = i,
                    Content = chunks[i],
                    Embedding = new Pgvector.Vector(vectors[i])
                });
            }

            doc.Status = "Ready";
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document {DocumentId} ingestion failed", docId);
            doc.Status = "Failed";
            await _db.SaveChangesAsync();
        }
    }
}
