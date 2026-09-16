using Experimento.Domain.Enums;

namespace Experimento.Domain.Entities;

/// <summary>
/// A single rationale item explaining part of a prediction.
/// </summary>
public class RationaleItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResultId { get; set; }
    public PredictionResult Result { get; set; } = null!;
    public RationaleCategory Category { get; set; }
    public string Claim { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string SourcesJson { get; set; } = "[]";
}
