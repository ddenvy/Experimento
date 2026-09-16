using Experimento.Domain.Entities;

namespace Experimento.Application.Abstractions;

/// <summary>
/// Append-only audit trail with hash-chain integrity verification.
/// </summary>
public interface IAuditTrail
{
    Task<AuditEntry> AppendAsync(Guid? actorUserId, string action, string entityType,
        string? entityId, string payloadJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the hash chain. Returns the id of the first broken entry, or null if intact.
    /// </summary>
    Task<long?> VerifyChainAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditEntry>> GetTrailAsync(string? entityType, string? entityId,
        int skip, int take, CancellationToken cancellationToken = default);
}
