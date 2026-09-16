using System.Security.Cryptography;
using System.Text;

namespace Experimento.Domain.Entities;

/// <summary>
/// An append-only audit entry linked to the previous one by a SHA-256 hash chain.
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
    /// Creates a new audit entry with a deterministic hash linked to the previous entry.
    /// </summary>
    public static AuditEntry Create(long id, Guid? actorUserId, string action, string entityType,
        string? entityId, string payloadJson, string previousHash)
    {
        var payloadHash = ComputeSha256Hex(payloadJson);
        var now = DateTime.UtcNow;
        // Truncate to microsecond precision to match PostgreSQL timestamptz storage,
        // otherwise the hash (computed with 7-digit ticks) won't survive a DB round-trip.
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
    /// Recomputes the entry hash and returns true if it matches the stored value.
    /// Also verifies that PayloadHash matches the actual PayloadJson content.
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
