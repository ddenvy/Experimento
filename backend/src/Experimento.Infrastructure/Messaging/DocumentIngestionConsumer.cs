using MassTransit;
using Microsoft.EntityFrameworkCore;
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
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ChunkingService _chunking;
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<DocumentIngestionConsumer> _logger;

    public DocumentIngestionConsumer(AppDbContext db, IDbContextFactory<AppDbContext> dbFactory,
        ChunkingService chunking, IEmbeddingService embedding, ILogger<DocumentIngestionConsumer> logger)
    {
        _db = db;
        _dbFactory = dbFactory;
        _chunking = chunking;
        _embedding = embedding;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IngestDocumentCommand> context)
    {
        var docId = context.Message.DocumentId;
        var doc = await _db.KnowledgeDocuments.FindAsync([docId]);
        if (doc is null) return;
        if (doc.Status == KnowledgeStatus.Ready) return; // повторная доставка уже обработанного документа

        try
        {
            doc.Status = KnowledgeStatus.Processing;
            await _db.SaveChangesAsync();

            var content = context.Message.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                doc.Status = KnowledgeStatus.Failed;
                await _db.SaveChangesAsync();
                return;
            }

            var chunks = _chunking.Chunk(content);
            if (chunks.Count == 0)
            {
                doc.Status = KnowledgeStatus.Failed;
                await _db.SaveChangesAsync();
                return;
            }

            // Эмбеддинги строятся одним пакетом — один сетевой вызов вместо N.
            var vectors = await _embedding.EmbedBatchAsync(chunks, context.CancellationToken);

            // Чанки и статус Ready — атомарно, с удалением старых чанков: повторная
            // доставка после частичной записи не создаёт второй комплект и не валится
            // на уникальном индексе (DocumentId, ChunkIndex).
            await using var tx = await _db.Database.BeginTransactionAsync(context.CancellationToken);
            await _db.KnowledgeChunks
                .Where(c => c.DocumentId == docId)
                .ExecuteDeleteAsync(context.CancellationToken);

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

            doc.Status = KnowledgeStatus.Ready;
            await _db.SaveChangesAsync(context.CancellationToken);
            await tx.CommitAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document {DocumentId} ingestion failed", docId);
            await MarkFailedAsync(docId);
        }
    }

    /// <summary>Пометка Failed через отдельный контекст (см. PredictionConsumer.MarkJobFailedAsync).</summary>
    private async Task MarkFailedAsync(Guid docId)
    {
        try
        {
            await using var errorDb = await _dbFactory.CreateDbContextAsync();
            await errorDb.KnowledgeDocuments
                .Where(d => d.Id == docId && d.Status != KnowledgeStatus.Ready)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, KnowledgeStatus.Failed));
        }
        catch (Exception persistEx)
        {
            _logger.LogCritical(persistEx, "Failed to mark document {DocumentId} as Failed", docId);
            throw;
        }
    }
}
