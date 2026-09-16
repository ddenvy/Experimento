using Experimento.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Experimento.Application.Abstractions;

/// <summary>
/// EF Core context contract exposed by Infrastructure.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Project> Projects { get; }
    DbSet<Formulation> Formulations { get; }
    DbSet<FormulationVersion> FormulationVersions { get; }
    DbSet<FormulationComponent> FormulationComponents { get; }
    DbSet<ModelRegistration> ModelRegistrations { get; }
    DbSet<PredictionJob> PredictionJobs { get; }
    DbSet<PredictionResult> PredictionResults { get; }
    DbSet<RationaleItem> RationaleItems { get; }
    DbSet<PredictionReview> PredictionReviews { get; }
    DbSet<ExperimentOutcome> ExperimentOutcomes { get; }
    DbSet<SimulationJob> SimulationJobs { get; }
    DbSet<SimulationResult> SimulationResults { get; }
    DbSet<SimulationCandidate> SimulationCandidates { get; }
    DbSet<KnowledgeDocument> KnowledgeDocuments { get; }
    DbSet<KnowledgeChunk> KnowledgeChunks { get; }
    DbSet<AuditEntry> AuditEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
