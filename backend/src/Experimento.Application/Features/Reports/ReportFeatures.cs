using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Experimento.Application.Features.Reports;

public record GenerateVersionReportQuery(Guid VersionId, Guid UserId = default) : IRequest<string>;

/// <summary>
/// Generates an inspection-ready Markdown report for a formulation version.
/// </summary>
public class GenerateVersionReportHandler : IRequestHandler<GenerateVersionReportQuery, string>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public GenerateVersionReportHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<string> Handle(GenerateVersionReportQuery request, CancellationToken ct)
    {
        if (!await _auth.OwnsVersionAsync(request.VersionId, request.UserId, ct))
            throw new ForbiddenException();

        var version = await _db.FormulationVersions
            .Include(v => v.Formulation)
            .Include(v => v.Components)
            .FirstOrDefaultAsync(v => v.Id == request.VersionId, ct)
            ?? throw new NotFoundException($"Version {request.VersionId} not found.");

        var jobs = await _db.PredictionJobs
            .Where(j => j.VersionId == version.Id)
            .Include(j => j.Result).ThenInclude(r => r!.RationaleItems)
            .Include(j => j.Result).ThenInclude(r => r!.Reviews)
            .Include(j => j.Result).ThenInclude(r => r!.Outcome)
            .ToListAsync(ct);

        var sims = await _db.SimulationJobs
            .Where(j => j.VersionId == version.Id)
            .Include(j => j.Result).ThenInclude(r => r!.Candidates)
            .ToListAsync(ct);

        var auditEntries = await _db.AuditEntries
            .Where(e => e.EntityType == "FormulationVersion" && e.EntityId == version.Id.ToString())
            .OrderBy(e => e.Id)
            .Take(100)
            .ToListAsync(ct);

        var md = new System.Text.StringBuilder();
        md.AppendLine($"# Formulation Report: {version.Formulation.Name} v{version.VersionNumber}");
        md.AppendLine();
        md.AppendLine($"**Target purpose:** {version.Formulation.TargetPurpose}");
        md.AppendLine($"**Created:** {version.CreatedAtUtc:O}");
        if (!string.IsNullOrEmpty(version.Notes)) md.AppendLine($"**Notes:** {version.Notes}");
        md.AppendLine();
        md.AppendLine("## Composition");
        md.AppendLine("| Component | CAS | Formula | Molar Mass | Proportion | Role |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (var c in version.Components)
            md.AppendLine($"| {c.ChemicalName} | {c.CasNumber ?? "-"} | {c.Formula ?? "-"} | {c.MolarMass} | {c.Proportion:P2} | {c.Role ?? "-"} |");
        md.AppendLine();
        md.AppendLine("## Conditions");
        md.AppendLine($"- Temperature: {version.Conditions.TemperatureCelsius} °C");
        if (version.Conditions.PressureKPa.HasValue) md.AppendLine($"- Pressure: {version.Conditions.PressureKPa} kPa");
        if (version.Conditions.PhTarget.HasValue) md.AppendLine($"- pH target: {version.Conditions.PhTarget}");
        if (!string.IsNullOrEmpty(version.Conditions.Solvent)) md.AppendLine($"- Solvent: {version.Conditions.Solvent}");
        if (!string.IsNullOrEmpty(version.Conditions.DeliveryTarget)) md.AppendLine($"- Delivery target: {version.Conditions.DeliveryTarget}");
        md.AppendLine();

        md.AppendLine("## Predictions");
        foreach (var job in jobs)
        {
            md.AppendLine($"### Job {job.Id} — {job.Status}");
            if (job.Result is null) continue;
            var r = job.Result;
            md.AppendLine($"- Success probability: {r.SuccessProbability:P1}");
            md.AppendLine($"- Toxicity score: {r.ToxicityScore:F2}");
            md.AppendLine($"- Stability score: {r.StabilityScore:F2}");
            md.AppendLine($"- Side risk: {r.SideRiskLevel}");
            md.AppendLine($"- Summary: {r.Summary}");
            md.AppendLine("#### Rationale");
            foreach (var item in r.RationaleItems)
            {
                md.AppendLine($"- **[{item.Category}]** {item.Claim} — {item.Explanation} (confidence {item.Confidence:P0})");
                var sources = JsonSerializer.Deserialize<List<RationaleSourceDto>>(item.SourcesJson) ?? new();
                foreach (var s in sources)
                    md.AppendLine($"  - Source: {s.Title} ({s.Reference}, similarity {s.Similarity:F2})");
            }
            if (r.Reviews.Count > 0)
            {
                md.AppendLine("#### Reviews");
                foreach (var rv in r.Reviews)
                    md.AppendLine($"- {rv.Decision}: {rv.Comment ?? ""} ({rv.CreatedAtUtc:O})");
            }
            if (r.Outcome is not null)
            {
                md.AppendLine("#### Actual outcome");
                md.AppendLine($"- Success: {r.Outcome.ActualSuccess}");
                md.AppendLine($"- Metrics: {r.Outcome.ActualMetricsJson}");
                md.AppendLine($"- Calibration error: {r.CalibrationError():F3}");
            }
            md.AppendLine();
        }

        md.AppendLine("## Simulations");
        foreach (var sim in sims)
        {
            md.AppendLine($"### Simulation {sim.Id} — {sim.Status}");
            if (sim.Result is null) continue;
            var sr = sim.Result;
            md.AppendLine($"- Iterations: {sr.IterationsExecuted}");
            md.AppendLine($"- Summary: {sr.Summary}");
            if (sr.BestCandidate is not null)
                md.AppendLine($"- Best candidate: rank {sr.BestCandidate.Rank}, success {sr.BestCandidate.SuccessProbability:P1}, params {sr.BestCandidate.ParametersJson}");
            md.AppendLine();
        }

        md.AppendLine("## Audit Trail");
        md.AppendLine("| Id | Timestamp | Actor | Action |");
        md.AppendLine("|---|---|---|---|");
        foreach (var e in auditEntries)
            md.AppendLine($"| {e.Id} | {e.TimestampUtc:O} | {e.ActorUserId} | {e.Action} |");
        md.AppendLine();
        md.AppendLine("*End of report.*");

        return md.ToString();
    }
}
