using System.Security.Cryptography;
using System.Text;

namespace Experimento.Domain.Entities;

/// <summary>
/// Неизменяемая запись, связанная с предыдущей цепочкой хешей SHA-256.
/// </summary>
public class AuditEntry
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string PayloadHash { get; set; } = string.Empty;
    public string PreviousHash { get; set; } = string.Empty;
    public string EntryHash { get; set; } = string.Empty;

    /// <summary>
    /// Создает запись с хешем, связанным с предыдущей записью.
    /// </summary>
    public static AuditEntry Create(long id, Guid? actorUserId, string action, string entityType,
        string? entityId, string payloadJson, string previousHash)
    {
        var payloadHash = ComputeSha256Hex(payloadJson);
        var now = DateTime.UtcNow;
        // Точность PostgreSQL равна микросекунде: одинаковое время нужно при проверке хеша.
        var timestamp = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond), DateTimeKind.Utc);
        var raw = $"{id}|{timestamp:O}|{actorUserId}|{action}|{entityType}|{entityId}|{payloadHash}|{previousHash}";
        var entryHash = ComputeSha256Hex(raw);

        return new AuditEntry
        {
            Id = id,
            TimestampUtc = timestamp,
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            PayloadJson = payloadJson,
            PayloadHash = payloadHash,
            PreviousHash = previousHash,
            EntryHash = entryHash
        };
    }

    /// <summary>
    /// Сверяет хеш записи и хеш содержимого с сохраненными значениями.
    /// </summary>
    public bool IsHashValid()
    {
        if (ComputeSha256Hex(PayloadJson) != PayloadHash)
            return false;
        var raw = $"{Id}|{TimestampUtc:O}|{ActorUserId}|{Action}|{EntityType}|{EntityId}|{PayloadHash}|{PreviousHash}";
        return ComputeSha256Hex(raw) == EntryHash;
    }

    private static string ComputeSha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
