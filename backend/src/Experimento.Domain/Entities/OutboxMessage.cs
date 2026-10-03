namespace Experimento.Domain.Entities;

/// <summary>Сообщение, сохраняемое вместе с задачей до отправки в брокер.</summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime AvailableAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LeaseUntilUtc { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }

    public static OutboxMessage Create(string kind, Guid entityId, string payloadJson, DateTime? availableAtUtc = null)
        => new()
        {
            Kind = kind,
            EntityId = entityId,
            PayloadJson = payloadJson,
            AvailableAtUtc = availableAtUtc ?? DateTime.UtcNow
        };
}
