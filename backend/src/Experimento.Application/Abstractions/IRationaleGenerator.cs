using Experimento.Domain.Entities;

namespace Experimento.Application.Abstractions;

/// <summary>
/// Generates structured rationale items with source attribution for a prediction.
/// </summary>
public interface IRationaleGenerator
{
    Task<IReadOnlyList<RationaleItem>> GenerateAsync(
        FormulationSnapshot snapshot,
        PredictionOutcome outcome,
        Guid resultId,
        CancellationToken cancellationToken = default);
}
