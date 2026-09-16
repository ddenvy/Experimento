namespace Experimento.Domain.Enums;

/// <summary>
/// Human-in-the-loop review decision on an AI prediction.
/// </summary>
public enum ReviewDecision
{
    Approved = 0,
    Rejected = 1,
    NeedsRevision = 2
}
