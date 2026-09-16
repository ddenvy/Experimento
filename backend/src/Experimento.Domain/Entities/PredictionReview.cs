using Experimento.Domain.Enums;

namespace Experimento.Domain.Entities;

/// <summary>
/// Human-in-the-loop review decision on a prediction.
/// </summary>
public class PredictionReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResultId { get; set; }
    public PredictionResult Result { get; set; } = null!;
    public Guid ReviewerUserId { get; set; }
    public User ReviewerUser { get; set; } = null!;
    public ReviewDecision Decision { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
