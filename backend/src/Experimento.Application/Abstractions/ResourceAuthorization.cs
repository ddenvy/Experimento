using Microsoft.EntityFrameworkCore;

namespace Experimento.Application.Abstractions;

/// <summary>
/// Единая точка проверки горизонтальных прав: пользователь имеет доступ только к тому,
/// что создано им (через цепочку Project → Formulation → Version → Job → Result).
/// Документы знаний с ProjectId == null считаются общедоступной глобальной базой.
/// </summary>
public sealed class ResourceAuthorization(IAppDbContext db)
{
    public Task<bool> OwnsProjectAsync(Guid projectId, Guid userId, CancellationToken ct) =>
        db.Projects.AnyAsync(p => p.Id == projectId && p.CreatedBy == userId, ct);

    public Task<bool> OwnsVersionAsync(Guid versionId, Guid userId, CancellationToken ct) =>
        db.FormulationVersions.AnyAsync(
            v => v.Id == versionId && v.Formulation.Project.CreatedBy == userId, ct);

    public Task<bool> OwnsFormulationAsync(Guid formulationId, Guid userId, CancellationToken ct) =>
        db.Formulations.AnyAsync(
            f => f.Id == formulationId && f.Project.CreatedBy == userId, ct);

    public Task<bool> OwnsPredictionJobAsync(Guid jobId, Guid userId, CancellationToken ct) =>
        db.PredictionJobs.AnyAsync(
            j => j.Id == jobId &&
                 (j.RequestedBy == userId || j.Version.Formulation.Project.CreatedBy == userId), ct);

    public Task<bool> OwnsPredictionResultAsync(Guid resultId, Guid userId, CancellationToken ct) =>
        db.PredictionResults.AnyAsync(
            r => r.Id == resultId &&
                 (r.Job.RequestedBy == userId || r.Job.Version.Formulation.Project.CreatedBy == userId), ct);

    public Task<bool> OwnsSimulationJobAsync(Guid jobId, Guid userId, CancellationToken ct) =>
        db.SimulationJobs.AnyAsync(
            j => j.Id == jobId &&
                 (j.RequestedBy == userId || j.Version.Formulation.Project.CreatedBy == userId), ct);

    public Task<bool> OwnsSimulationResultAsync(Guid resultId, Guid userId, CancellationToken ct) =>
        db.SimulationResults.AnyAsync(
            r => r.Id == resultId &&
                 (r.Job.RequestedBy == userId || r.Job.Version.Formulation.Project.CreatedBy == userId), ct);

    /// <summary>
    /// Документ доступен, если он глобальный (ProjectId == null), загружен этим пользователем
    /// либо принадлежит проекту, владельцем которого он является.
    /// </summary>
    public Task<bool> CanAccessDocumentAsync(Guid documentId, Guid userId, CancellationToken ct) =>
        db.KnowledgeDocuments.AnyAsync(
            d => d.Id == documentId &&
                 (d.ProjectId == null ||
                  d.UploadedBy == userId ||
                  db.Projects.Any(p => p.Id == d.ProjectId && p.CreatedBy == userId)), ct);
}
