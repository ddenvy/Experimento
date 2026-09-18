namespace Experimento.Domain.Enums;

/// <summary>
/// Lifecycle status of a knowledge-base document ingestion.
/// Stored as text (HasConversion) so database rows stay human-readable.
/// </summary>
public enum KnowledgeStatus
{
    Pending,
    Processing,
    Ready,
    Failed
}
