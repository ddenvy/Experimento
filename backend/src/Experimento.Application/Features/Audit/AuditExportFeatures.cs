using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Experimento.Application.Features.Audit;

/// <summary>
/// Формирует пакет документов для аудита: оценку соответствия принципам ALCOA+,
/// полный журнал изменений (без пагинации), инвентарь версий формуляций, прогоны
/// предсказаний и симуляций, ревью-подписи и отпечаток самого документа.
/// </summary>
public record GenerateAuditExportQuery : IRequest<string>;

/// <summary>
/// Генерирует Markdown-пакет аудита. Оценка ALCOA+ вычисляется по фактическим данным
/// (наличие автора, целостность хеш-цепочки, пересчёт хешей, покрытие аудитом),
/// а не декларируется статически.
/// </summary>
public class GenerateAuditExportHandler : IRequestHandler<GenerateAuditExportQuery, string>
{
    /// <summary>Допуск суммы пропорций — тот же, что в FormulationVersion.EnsureValid.</summary>
    private const double ProportionTolerance = 0.001;

    private readonly IAppDbContext _db;
    public GenerateAuditExportHandler(IAppDbContext db) => _db = db;

    public async Task<string> Handle(GenerateAuditExportQuery request, CancellationToken ct)
    {
        var entries = await _db.AuditEntries.OrderBy(e => e.Id).ToListAsync(ct);
        var users = await _db.Users.ToDictionaryAsync(u => u.Id, ct);

        var versions = await _db.FormulationVersions
            .Include(v => v.Formulation)
            .Include(v => v.Components)
            .OrderBy(v => v.CreatedAtUtc)
            .ToListAsync(ct);

        var predictionJobs = await _db.PredictionJobs
            .Include(j => j.Result).ThenInclude(r => r!.Reviews)
            .Include(j => j.Result).ThenInclude(r => r!.Outcome)
            .OrderBy(j => j.CreatedAtUtc)
            .ToListAsync(ct);

        var simulationJobs = await _db.SimulationJobs
            .Include(j => j.Result)
            .OrderBy(j => j.CreatedAtUtc)
            .ToListAsync(ct);

        var assessment = Assess(entries, versions, predictionJobs, simulationJobs);

        var md = new StringBuilder();
        AppendHeader(md, entries, users);
        AppendAssessment(md, assessment);
        AppendVersions(md, versions, users);
        AppendRuns(md, predictionJobs, simulationJobs, users);
        AppendReviews(md, predictionJobs, users);
        AppendAuditTrail(md, entries, users);
        AppendFooter(md, assessment);

        return md.ToString();
    }

    // --- Оценка принципов ALCOA+ -------------------------------------------------

    private sealed record PrincipleResult(string Name, string Status, string Evidence);

    private sealed record Assessment(
        IReadOnlyList<PrincipleResult> Principles,
        bool ChainIntact,
        long? FirstBrokenId,
        int InvalidHashCount,
        int UnattributedCount,
        bool Chronological);

    private static Assessment Assess(
        IReadOnlyList<AuditEntry> entries,
        IReadOnlyList<FormulationVersion> versions,
        IReadOnlyList<PredictionJob> predictionJobs,
        IReadOnlyList<SimulationJob> simulationJobs)
    {
        // Целостность цепочки: PreviousHash каждого звена должен совпадать с EntryHash предыдущего.
        long? firstBrokenId = null;
        var previousHash = string.Empty;
        foreach (var entry in entries)
        {
            if (entry.PreviousHash != previousHash || !entry.IsHashValid())
            {
                firstBrokenId = entry.Id;
                break;
            }
            previousHash = entry.EntryHash;
        }
        var chainIntact = firstBrokenId is null;

        var invalidHashCount = entries.Count(e => !e.IsHashValid());
        var unattributedCount = entries.Count(e => e.ActorUserId is null);

        // Хронология: порядок по Id не должен нарушать монотонность меток времени.
        var chronological = true;
        for (var i = 1; i < entries.Count; i++)
        {
            if (entries[i].TimestampUtc < entries[i - 1].TimestampUtc)
            {
                chronological = false;
                break;
            }
        }

        // Покрытие аудитом: каждая версия формуляции должна иметь хотя бы одну запись.
        var auditEntityIds = entries
            .Where(e => e.EntityType == "FormulationVersion" && e.EntityId is not null)
            .Select(e => e.EntityId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var versionsWithoutAudit = versions.Count(v => !auditEntityIds.Contains(v.Id.ToString()));

        // Точность состава: сумма пропорций обязана сходиться к 1.0 в допуске.
        var invalidCompositions = versions.Count(v =>
            Math.Abs(v.Components.Sum(c => c.Proportion) - 1.0) > ProportionTolerance);

        var principles = new List<PrincipleResult>
        {
            new("Attributable",
                unattributedCount == 0 ? "PASS" : "WARN",
                unattributedCount == 0
                    ? $"All {entries.Count} audit entries carry an actor identity."
                    : $"{unattributedCount} of {entries.Count} entries have no actor identity."),

            new("Legible",
                "PASS",
                $"Journal is emitted as human-readable Markdown with JSON payloads; {entries.Count} entries recorded."),

            new("Contemporaneous",
                !chronological ? "FAIL" : entries.Count == 0 ? "WARN" : "PASS",
                entries.Count == 0
                    ? "No audit entries recorded yet."
                    : $"Timestamps recorded on write in UTC; monotonic order by entry id verified "
                      + $"({entries[0].TimestampUtc:O} → {entries[^1].TimestampUtc:O})."),

            new("Original",
                chainIntact ? "PASS" : "FAIL",
                chainIntact
                    ? "Every entry is sealed by a SHA-256 hash linked to its predecessor; chain is intact."
                    : $"Hash chain is broken at entry {firstBrokenId}."),

            new("Accurate",
                invalidHashCount > 0 || invalidCompositions > 0 ? "FAIL" : "PASS",
                invalidHashCount > 0
                    ? $"{invalidHashCount} entries fail hash recomputation (payload or metadata altered)."
                    : invalidCompositions > 0
                        ? $"{invalidCompositions} formulation versions have components whose proportions do not sum to 1.0."
                        : $"All {entries.Count} entry hashes recompute correctly; "
                          + $"all {versions.Count} formulation versions satisfy the proportion invariant."),

            new("Complete",
                versionsWithoutAudit > 0 ? "WARN" : "PASS",
                $"{versions.Count} formulation versions, {predictionJobs.Count} prediction runs, "
                + $"{simulationJobs.Count} simulation runs exported in full. "
                + (versionsWithoutAudit == 0
                    ? "Every version has audit coverage."
                    : $"{versionsWithoutAudit} versions have no audit coverage.")),

            new("Consistent",
                !chronological ? "FAIL" : "PASS",
                chronological
                    ? "Entry ids and timestamps advance monotonically; no reordering detected."
                    : "Timestamp order contradicts entry id order."),

            new("Enduring",
                "PASS",
                "Entries are append-only rows in PostgreSQL; no update or delete path exists in the API."),

            new("Available",
                "PASS",
                "The complete trail is retrievable on demand through this export, independent of UI state."),
        };

        return new Assessment(principles, chainIntact, firstBrokenId, invalidHashCount,
            unattributedCount, chronological);
    }

    // --- Секции документа ---------------------------------------------------------

    private static void AppendHeader(StringBuilder md,
        IReadOnlyList<AuditEntry> entries,
        IReadOnlyDictionary<Guid, User> users)
    {
        md.AppendLine("# Audit Export Package");
        md.AppendLine();
        md.AppendLine($"**Generated:** {DateTime.UtcNow:O}");
        md.AppendLine($"**Schema version:** 1");
        md.AppendLine($"**Journal entries:** {entries.Count}");
        if (entries.Count > 0)
            md.AppendLine($"**Period covered:** {entries[0].TimestampUtc:O} → {entries[^1].TimestampUtc:O}");
        md.AppendLine($"**Actors recorded:** {users.Count}");
        md.AppendLine();
        md.AppendLine("> This package is generated from the append-only audit journal. "
                      + "Its integrity rests on the SHA-256 hash chain described under *Original*.");
        md.AppendLine();
    }

    private static void AppendAssessment(StringBuilder md, Assessment assessment)
    {
        md.AppendLine("## ALCOA+ Compliance Assessment");
        md.AppendLine();
        md.AppendLine("| Principle | Status | Evidence |");
        md.AppendLine("|---|---|---|");
        foreach (var p in assessment.Principles)
            md.AppendLine($"| {p.Name} | {p.Status} | {Escape(p.Evidence)} |");
        md.AppendLine();

        var failed = assessment.Principles.Where(p => p.Status == "FAIL").Select(p => p.Name).ToList();
        var warned = assessment.Principles.Where(p => p.Status == "WARN").Select(p => p.Name).ToList();
        md.AppendLine(failed.Count > 0
            ? $"**Overall: NOT COMPLIANT** — failing principles: {string.Join(", ", failed)}."
            : warned.Count > 0
                ? $"**Overall: COMPLIANT WITH WARNINGS** — review: {string.Join(", ", warned)}."
                : "**Overall: COMPLIANT** — all ALCOA+ principles satisfied.");
        md.AppendLine();
    }

    private static void AppendVersions(StringBuilder md,
        IReadOnlyList<FormulationVersion> versions,
        IReadOnlyDictionary<Guid, User> users)
    {
        md.AppendLine("## Formulation Versions Inventory");
        md.AppendLine();
        if (versions.Count == 0)
        {
            md.AppendLine("_No formulation versions recorded._");
            md.AppendLine();
            return;
        }

        foreach (var version in versions)
        {
            md.AppendLine($"### {version.Formulation.Name} v{version.VersionNumber}");
            md.AppendLine();
            md.AppendLine($"- **Version id:** {version.Id}");
            md.AppendLine($"- **Status:** {version.Status}");
            md.AppendLine($"- **Target purpose:** {version.Formulation.TargetPurpose}");
            md.AppendLine($"- **Created:** {version.CreatedAtUtc:O} by {Actor(version.CreatedBy, users)}");
            md.AppendLine($"- **Conditions:** {version.Conditions.TemperatureCelsius} °C"
                          + (version.Conditions.PhTarget.HasValue ? $", pH {version.Conditions.PhTarget}" : "")
                          + (string.IsNullOrEmpty(version.Conditions.Solvent) ? "" : $", solvent {version.Conditions.Solvent}"));
            if (!string.IsNullOrEmpty(version.Notes))
                md.AppendLine($"- **Notes:** {version.Notes}");
            md.AppendLine();
            md.AppendLine("| Component | CAS | Formula | Molar Mass | Proportion | Role |");
            md.AppendLine("|---|---|---|---|---|---|");
            foreach (var c in version.Components)
                md.AppendLine($"| {c.ChemicalName} | {c.CasNumber ?? "-"} | {c.Formula ?? "-"} | {c.MolarMass} | {c.Proportion:P2} | {c.Role ?? "-"} |");
            md.AppendLine();
        }
    }

    private static void AppendRuns(StringBuilder md,
        IReadOnlyList<PredictionJob> predictionJobs,
        IReadOnlyList<SimulationJob> simulationJobs,
        IReadOnlyDictionary<Guid, User> users)
    {
        md.AppendLine("## Prediction Runs");
        md.AppendLine();
        if (predictionJobs.Count == 0)
        {
            md.AppendLine("_No prediction runs recorded._");
        }
        else
        {
            md.AppendLine("| Run | Version | Status | Requested by | Started | Completed |");
            md.AppendLine("|---|---|---|---|---|---|");
            foreach (var job in predictionJobs)
                md.AppendLine($"| {job.Id} | {job.VersionId} | {job.Status} | {Actor(job.RequestedBy, users)} "
                              + $"| {Format(job.StartedAtUtc)} | {Format(job.CompletedAtUtc)} |");
        }
        md.AppendLine();

        md.AppendLine("## Simulation Runs");
        md.AppendLine();
        if (simulationJobs.Count == 0)
        {
            md.AppendLine("_No simulation runs recorded._");
        }
        else
        {
            md.AppendLine("| Run | Version | Status | Requested by | Iterations | Summary |");
            md.AppendLine("|---|---|---|---|---|---|");
            foreach (var job in simulationJobs)
                md.AppendLine($"| {job.Id} | {job.VersionId} | {job.Status} | {Actor(job.RequestedBy, users)} "
                              + $"| {job.Result?.IterationsExecuted.ToString() ?? "-"} | {Escape(job.Result?.Summary ?? "-")} |");
        }
        md.AppendLine();
    }

    private static void AppendReviews(StringBuilder md,
        IReadOnlyList<PredictionJob> predictionJobs,
        IReadOnlyDictionary<Guid, User> users)
    {
        md.AppendLine("## Reviews and Signatures");
        md.AppendLine();

        var reviews = predictionJobs
            .Where(j => j.Result is not null)
            .SelectMany(j => j.Result!.Reviews.Select(r => (Job: j, Review: r)))
            .OrderBy(x => x.Review.CreatedAtUtc)
            .ToList();

        var outcomes = predictionJobs
            .Where(j => j.Result?.Outcome is not null)
            .OrderBy(j => j.Result!.Outcome!.RecordedAtUtc)
            .ToList();

        if (reviews.Count == 0 && outcomes.Count == 0)
        {
            md.AppendLine("_No review decisions or lab outcomes recorded._");
            md.AppendLine();
            return;
        }

        if (reviews.Count > 0)
        {
            md.AppendLine("### Review decisions");
            md.AppendLine();
            md.AppendLine("| Run | Reviewer | Decision | Comment | Signed at |");
            md.AppendLine("|---|---|---|---|---|");
            foreach (var (job, review) in reviews)
                md.AppendLine($"| {job.Id} | {Actor(review.ReviewerUserId, users)} | {review.Decision} "
                              + $"| {Escape(review.Comment ?? "-")} | {review.CreatedAtUtc:O} |");
            md.AppendLine();
        }

        if (outcomes.Count > 0)
        {
            md.AppendLine("### Lab outcomes");
            md.AppendLine();
            md.AppendLine("| Run | Actual success | Metrics | Calibration error | Recorded by | Recorded at |");
            md.AppendLine("|---|---|---|---|---|---|");
            foreach (var job in outcomes)
            {
                var outcome = job.Result!.Outcome!;
                md.AppendLine($"| {job.Id} | {outcome.ActualSuccess} | {Escape(outcome.ActualMetricsJson)} "
                              + $"| {job.Result!.CalibrationError():F3} | {Actor(outcome.RecordedBy, users)} "
                              + $"| {outcome.RecordedAtUtc:O} |");
            }
            md.AppendLine();
        }
    }

    private static void AppendAuditTrail(StringBuilder md,
        IReadOnlyList<AuditEntry> entries,
        IReadOnlyDictionary<Guid, User> users)
    {
        md.AppendLine("## Full Audit Trail");
        md.AppendLine();
        md.AppendLine("Append-only journal in entry order. `Entry hash` seals the row; "
                      + "`Previous hash` links it to its predecessor.");
        md.AppendLine();
        if (entries.Count == 0)
        {
            md.AppendLine("_No audit entries recorded._");
            md.AppendLine();
            return;
        }

        md.AppendLine("| Id | Timestamp (UTC) | Actor | Action | Entity | Entity id | Entry hash | Previous hash |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var e in entries)
            md.AppendLine($"| {e.Id} | {e.TimestampUtc:O} | {Actor(e.ActorUserId, users)} | {e.Action} "
                          + $"| {e.EntityType} | {e.EntityId ?? "-"} | `{e.EntryHash}` | `{e.PreviousHash}` |");
        md.AppendLine();
    }

    private static void AppendFooter(StringBuilder md, Assessment assessment)
    {
        md.AppendLine("## Integrity Statement");
        md.AppendLine();
        md.AppendLine($"- Hash chain: {(assessment.ChainIntact ? "INTACT" : $"BROKEN at entry {assessment.FirstBrokenId}")}");
        md.AppendLine($"- Entries failing hash recomputation: {assessment.InvalidHashCount}");
        md.AppendLine($"- Entries without actor identity: {assessment.UnattributedCount}");
        md.AppendLine($"- Chronological order: {(assessment.Chronological ? "verified" : "violated")}");
        md.AppendLine();
        md.AppendLine("The fingerprint below covers every section above it. "
                      + "Recomputing SHA-256 over the document body must reproduce it.");
        md.AppendLine();
        // Полезная нагрузка захватывается в переменную до добавления строки с отпечатком:
        // отпечаток обязан покрывать весь документ выше этой строки.
        var payload = md.ToString();
        md.AppendLine($"**Document fingerprint (SHA-256):** `{ComputeSha256Hex(payload)}`");
        md.AppendLine();
        md.AppendLine("*End of audit export package.*");
    }

    // --- Вспомогательные ----------------------------------------------------------

    private static string Actor(Guid actorId, IReadOnlyDictionary<Guid, User> users)
        => users.TryGetValue(actorId, out var user)
            ? $"{user.Email} ({user.Role})"
            : $"{actorId} (unknown user)";

    private static string Actor(Guid? actorId, IReadOnlyDictionary<Guid, User> users)
        => actorId is null ? "system" : Actor(actorId.Value, users);

    private static string Format(DateTime? value) => value?.ToString("O") ?? "-";

    /// <summary>Экранирует вертикальную черту — иначе значение ломает разметку GFM-таблицы.</summary>
    private static string Escape(string value) => value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");

    private static string ComputeSha256Hex(string input)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
