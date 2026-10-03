using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Experimento.Infrastructure.Data;
using Experimento.Infrastructure.Knowledge;
using System.Text.Json;

namespace Experimento.Infrastructure.Messaging;

/// <summary>
/// Разбивает документ на фрагменты и строит векторы для каждого фрагмента.
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
        var claimed = await _db.KnowledgeDocuments
            .Where(d => d.Id == docId && d.Status == KnowledgeStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, KnowledgeStatus.Processing)
                .SetProperty(d => d.StartedAtUtc, DateTime.UtcNow), context.CancellationToken);
        if (claimed == 0) return;
        var doc = await _db.KnowledgeDocuments.FindAsync([docId], context.CancellationToken);
        if (doc is null) return;

        try
        {
            var content = context.Message.Content;
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("Document content is empty.");

            var chunks = _chunking.Chunk(content);
            if (chunks.Count == 0)
                throw new InvalidOperationException("Document has no indexable chunks.");

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
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document {DocumentId} ingestion failed", docId);
            await ScheduleRetryOrFailAsync(context.Message);
        }
    }

    /// <summary>Сохраняет повтор вместе с состоянием документа в одной транзакции.</summary>
    private async Task ScheduleRetryOrFailAsync(IngestDocumentCommand command)
    {
        try
        {
            await using var errorDb = await _dbFactory.CreateDbContextAsync();
            await using var tx = await errorDb.Database.BeginTransactionAsync();
            var rows = await errorDb.KnowledgeDocuments
                .FromSqlInterpolated($@"SELECT * FROM ""KnowledgeDocuments"" WHERE ""Id"" = {command.DocumentId} FOR UPDATE")
                .ToListAsync();
            var current = rows.SingleOrDefault();
            if (current is null || current.Status != KnowledgeStatus.Processing) return;

            current.AttemptCount++;
            if (current.AttemptCount >= JobRetryPolicy.MaxAttempts)
            {
                current.Status = KnowledgeStatus.Failed;
            }
            else
            {
                current.Status = KnowledgeStatus.Pending;
                current.StartedAtUtc = null;
                errorDb.OutboxMessages.Add(OutboxMessage.Create(OutboxKinds.Document, command.DocumentId,
                    JsonSerializer.Serialize(command),
                    DateTime.UtcNow.Add(JobRetryPolicy.Delay(current.AttemptCount))));
            }
            await errorDb.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception persistEx)
        {
            _logger.LogCritical(persistEx, "Failed to schedule document {DocumentId} retry", command.DocumentId);
            throw;
        }
    }
}
