using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Experimento.Infrastructure.Data;

/// <summary>
/// EF Core DbContext implementing IAppDbContext with PostgreSQL + pgvector.
/// </summary>
public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Formulation> Formulations => Set<Formulation>();
    public DbSet<FormulationVersion> FormulationVersions => Set<FormulationVersion>();
    public DbSet<FormulationComponent> FormulationComponents => Set<FormulationComponent>();
    public DbSet<ModelRegistration> ModelRegistrations => Set<ModelRegistration>();
    public DbSet<PredictionJob> PredictionJobs => Set<PredictionJob>();
    public DbSet<PredictionResult> PredictionResults => Set<PredictionResult>();
    public DbSet<RationaleItem> RationaleItems => Set<RationaleItem>();
    public DbSet<PredictionReview> PredictionReviews => Set<PredictionReview>();
    public DbSet<ExperimentOutcome> ExperimentOutcomes => Set<ExperimentOutcome>();
    public DbSet<SimulationJob> SimulationJobs => Set<SimulationJob>();
    public DbSet<SimulationResult> SimulationResults => Set<SimulationResult>();
    public DbSet<SimulationCandidate> SimulationCandidates => Set<SimulationCandidate>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        // User
        modelBuilder.Entity<User>(b =>
        {
            b.HasIndex(u => u.Email).IsUnique();
        });

        // FormulationVersion owns Conditions
        modelBuilder.Entity<FormulationVersion>(b =>
        {
            b.OwnsOne(v => v.Conditions);
            b.HasIndex(v => new { v.FormulationId, v.VersionNumber }).IsUnique();
        });

        // KnowledgeChunk embedding
        modelBuilder.Entity<KnowledgeChunk>(b =>
        {
            b.Property(c => c.Embedding)
                .HasColumnType("vector(1536)");
            b.HasIndex(c => c.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        });

        // AuditEntry - append-only, hash chain
        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).UseIdentityAlwaysColumn();
            b.HasIndex(e => new { e.EntityType, e.EntityId });
        });

        // Unique index on refresh token hash
        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.HasIndex(t => t.TokenHash).IsUnique();
        });

        // Simulation relationships (disambiguate Candidates vs BestCandidate)
        modelBuilder.Entity<SimulationResult>(b =>
        {
            b.HasMany(r => r.Candidates)
                .WithOne(c => c.Result)
                .HasForeignKey(c => c.ResultId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(r => r.BestCandidate)
                .WithOne()
                .HasForeignKey<SimulationResult>(r => r.BestCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Индексы под частые фильтры и навигации по внешним ключам (N+1/seq scan защита).
        modelBuilder.Entity<PredictionJob>(b =>
            b.HasIndex(j => new { j.VersionId, j.RequestedBy }));
        modelBuilder.Entity<PredictionResult>(b =>
            b.HasIndex(r => r.JobId).IsUnique());
        modelBuilder.Entity<RationaleItem>(b =>
            b.HasIndex(r => r.ResultId));
        modelBuilder.Entity<SimulationJob>(b =>
            b.HasIndex(j => new { j.VersionId, j.RequestedBy }));
        modelBuilder.Entity<SimulationResult>(b =>
            b.HasIndex(r => r.JobId).IsUnique());
        modelBuilder.Entity<SimulationCandidate>(b =>
            b.HasIndex(c => c.ResultId));
        modelBuilder.Entity<KnowledgeChunk>(b =>
            b.HasIndex(c => new { c.DocumentId, c.ChunkIndex }).IsUnique());
        modelBuilder.Entity<AuditEntry>(b =>
            b.HasIndex(e => e.ActorUserId));

        base.OnModelCreating(modelBuilder);
    }
}
