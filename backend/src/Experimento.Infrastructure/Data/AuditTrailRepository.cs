using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Experimento.Infrastructure.Data;

/// <summary>
/// Append-only audit trail with SHA-256 hash-chain integrity verification.
/// </summary>
public class AuditTrailRepository : IAuditTrail
{
    // Фиксированный ключ транзакционной advisory-блокировки для сериализации записи в журнал.
    private const long AdvisoryLockKey = 0x4558_5045_5249_4D21;

    private readonly AppDbContext _db;
    public AuditTrailRepository(AppDbContext db) => _db = db;

    public async Task<AuditEntry> AppendAsync(Guid? actorUserId, string action, string entityType,
        string? entityId, string payloadJson, CancellationToken cancellationToken = default)
    {
        // Всё добавление сериализуется транзакционным advisory-блокировкой PostgreSQL,
        // чтобы параллельные запросы не получили один и тот же PreviousHash и не разорвали цепочку.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(@key)", [new NpgsqlParameter("key", AdvisoryLockKey)], cancellationToken);

        var lastHash = await _db.AuditEntries
            .OrderByDescending(e => e.Id)
            .Select(e => e.EntryHash)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        // Id присваивается identity-колонкой после save: сначала считаем хеш с заглушкой, затем пересчитываем.
        var entry = AuditEntry.Create(0, actorUserId, action, entityType, entityId, payloadJson, lastHash);
        _db.AuditEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);

        // Пересчёт хеша с реальным identity-Id.
        var actual = AuditEntry.Create(entry.Id, actorUserId, action, entityType, entityId, payloadJson, lastHash);
        entry.PayloadHash = actual.PayloadHash;
        entry.EntryHash = actual.EntryHash;
        entry.TimestampUtc = actual.TimestampUtc;
        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return entry;
    }

    public async Task<long?> VerifyChainAsync(CancellationToken cancellationToken = default)
    {
        // Весь журнал в память не грузим — идём батчами по Id (keyset pagination).
        const int batchSize = 1000;
        long lastId = 0;
        string previousHash = string.Empty;

        while (true)
        {
            var batch = await _db.AuditEntries
                .AsNoTracking()
                .Where(e => e.Id > lastId)
                .OrderBy(e => e.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
                return null;

            foreach (var entry in batch)
            {
                if (entry.PreviousHash != previousHash)
                    return entry.Id;
                if (!entry.IsHashValid())
                    return entry.Id;
                previousHash = entry.EntryHash;
            }

            lastId = batch[^1].Id;
            if (batch.Count < batchSize)
                return null;
        }
    }

    public async Task<IReadOnlyList<AuditEntry>> GetTrailAsync(string? entityType, string? entityId,
        int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = _db.AuditEntries.AsQueryable();
        if (!string.IsNullOrEmpty(entityType))
            query = query.Where(e => e.EntityType == entityType);
        if (!string.IsNullOrEmpty(entityId))
            query = query.Where(e => e.EntityId == entityId);
        return await query.OrderByDescending(e => e.Id).Skip(skip).Take(take).ToListAsync(cancellationToken);
    }
}
