using Experimento.Domain.Enums;

namespace Experimento.Domain.Entities;

/// <summary>
/// The outcome of a prediction job.
/// </summary>
public class PredictionResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public PredictionJob Job { get; set; } = null!;
    public Guid ModelRegistrationId { get; set; }
    public ModelRegistration ModelRegistration { get; set; } = null!;
    public double SuccessProbability { get; set; }
    public double ToxicityScore { get; set; }
    public double StabilityScore { get; set; }
    public SideRiskLevel SideRiskLevel { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<RationaleItem> RationaleItems { get; set; } = new List<RationaleItem>();
    public ICollection<PredictionReview> Reviews { get; set; } = new List<PredictionReview>();
    public ExperimentOutcome? Outcome { get; set; }

    /// <summary>
    /// Absolute calibration error between predicted and actual success.
    /// </summary>
    public double CalibrationError()
    {
        if (Outcome is null) return 0;
        return Math.Abs(SuccessProbability - (Outcome.ActualSuccess ? 1.0 : 0.0));
    }
}
