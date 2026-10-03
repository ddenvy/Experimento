using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Experimento.Infrastructure.Data;

/// <summary>
/// Неизменяемый журнал с цепочкой SHA-256 и проверкой целостности.
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
        if (_db.Database.CurrentTransaction is not null)
            return await AppendInTransactionAsync(actorUserId, action, entityType, entityId, payloadJson, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var entry = await AppendInTransactionAsync(actorUserId, action, entityType, entityId, payloadJson, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entry;
    }

    private async Task<AuditEntry> AppendInTransactionAsync(Guid? actorUserId, string action, string entityType,
        string? entityId, string payloadJson, CancellationToken cancellationToken)
    {
        // Блокировка действует до коммита бизнес-операции и не допускает разветвления цепочки.
        await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(@key)", [new NpgsqlParameter("key", AdvisoryLockKey)], cancellationToken);

        var lastHash = await _db.AuditEntries
            .OrderByDescending(e => e.Id)
            .Select(e => e.EntryHash)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        // Sequence выдает Id до вставки: запись с окончательным хешем создается один раз.
        var id = await _db.Database.SqlQueryRaw<long>(
                "SELECT nextval(pg_get_serial_sequence('\"AuditEntries\"', 'Id')) AS \"Value\"")
            .SingleAsync(cancellationToken);
        var entry = AuditEntry.Create(id, actorUserId, action, entityType, entityId, payloadJson, lastHash);
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""AuditEntries""
                (""Id"", ""TimestampUtc"", ""ActorUserId"", ""Action"", ""EntityType"", ""EntityId"",
                 ""PayloadJson"", ""PayloadHash"", ""PreviousHash"", ""EntryHash"")
            OVERRIDING SYSTEM VALUE
            VALUES ({entry.Id}, {entry.TimestampUtc}, {entry.ActorUserId}, {entry.Action}, {entry.EntityType},
                    {entry.EntityId}, {entry.PayloadJson}, {entry.PayloadHash}, {entry.PreviousHash},
                    {entry.EntryHash})", cancellationToken);
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
        var query = _db.AuditEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(entityType))
            query = query.Where(e => e.EntityType == entityType);
        if (!string.IsNullOrEmpty(entityId))
            query = query.Where(e => e.EntityId == entityId);
        return await query.OrderByDescending(e => e.Id).Skip(skip).Take(take).ToListAsync(cancellationToken);
    }
}
