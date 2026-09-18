using Experimento.Domain.Entities;
using Experimento.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace Experimento.Application.Features.Demo;

/// <summary>
/// Создаёт для указанного пользователя готовый demo-проект с полным научным циклом:
/// рецептура, прогноз с ревью и lab outcome, симуляция, исследование стабильности,
/// пара документов в Knowledge Base. Идемпотентно: если у пользователя уже есть
/// проект с именем DemoProjectName, возвращает существующий.
/// </summary>
public record ProvisionDemoDataCommand(Guid UserId) : IRequest<DemoDataDto>;

public record DemoDataDto(Guid ProjectId, Guid FormulationId, int FormulationVersions);

/// <summary>
/// Обработчик provision demo-данных. Все данные создаются напрямую в БД,
/// без очередей и внешних вызовов — детерминированно и за один вызов.
/// </summary>
public class ProvisionDemoDataHandler : IRequestHandler<ProvisionDemoDataCommand, DemoDataDto>
{
    private readonly IAppDbContext _db;
    public ProvisionDemoDataHandler(IAppDbContext db) => _db = db;

    private const string DemoProjectName = "Demo: Aspirin Tablet 500mg";
    private const string DemoFormulationName = "Aspirin IR Tablet 500 mg";

    public async Task<DemoDataDto> Handle(ProvisionDemoDataCommand request, CancellationToken ct)
    {
        // Идемпотентность: ищем существующий demo-проект.
        var existing = await _db.Projects
            .AsNoTracking()
            .Where(p => p.CreatedBy == request.UserId && p.Name == DemoProjectName)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            var formulations = await _db.Formulations
                .AsNoTracking()
                .Where(f => f.ProjectId == existing.Id)
                .Select(f => new { f.Id, Versions = f.Versions.Count })
                .FirstOrDefaultAsync(ct);

            return new DemoDataDto(
                existing.Id,
                formulations?.Id ?? Guid.Empty,
                formulations?.Versions ?? 0);
        }

        // 1. Проект
        var project = new Project
        {
            Name = DemoProjectName,
            Description = "Demo project with a complete R&D cycle: formulation, prediction, "
                        + "lab outcome, stability study and knowledge base documents.",
            CreatedBy = request.UserId
        };
        _db.Projects.Add(project);

        // 2. Формула
        var formulation = new Formulation
        {
            ProjectId = project.Id,
            Name = DemoFormulationName,
            TargetPurpose = "Immediate-release oral tablet for mild to moderate pain relief"
        };
        _db.Formulations.Add(formulation);

        // 3. Версия v1
        var version = new FormulationVersion
        {
            FormulationId = formulation.Id,
            VersionNumber = 1,
            Status = FormulationStatus.Draft,
            Notes = "Initial prototype formulation — standard IR tablet blend.",
            CreatedBy = request.UserId,
            Conditions = new FormulationConditions
            {
                TemperatureCelsius = 25,
                PressureKPa = 101.3,
                PhTarget = 6.8,
                Solvent = "water"
            },
            Components = new List<FormulationComponent>
            {
                new() { ChemicalName = "Aspirin", CasNumber = "50-78-2", Formula = "C9H8O4",
                        MolarMass = 180.16, Proportion = 0.70, Role = "Active Pharmaceutical Ingredient",
                        PubChemCid = 2244 },
                new() { ChemicalName = "Microcrystalline cellulose", CasNumber = "9004-34-6",
                        Formula = "(C6H10O5)n", MolarMass = 36_000, Proportion = 0.20,
                        Role = "Diluent / Binder", PubChemCid = 11129 },
                new() { ChemicalName = "Starch (maize)", CasNumber = "9005-25-8",
                        Formula = "(C6H10O5)n", MolarMass = 70_000, Proportion = 0.08,
                        Role = "Disintegrant", PubChemCid = 22999 },
                new() { ChemicalName = "Magnesium stearate", CasNumber = "557-04-0",
                        Formula = "C36H70MgO4", MolarMass = 591.24, Proportion = 0.02,
                        Role = "Lubricant", PubChemCid = 6936 },
            }
        };
        _db.FormulationVersions.Add(version);

        // 4. Модель (должна быть в БД из DbSeeder; подстраховываемся)
        var model = await _db.ModelRegistrations
            .FirstOrDefaultAsync(m => m.Name == "rule-based" && m.Version == "v2", ct);
        if (model is null)
        {
            model = new ModelRegistration
            {
                Name = "rule-based",
                Version = "v2",
                Description = "Deterministic heuristic predictor with structure-based rules.",
                ContextOfUse = "Pre-formulation screening, ranking candidates, stability trend estimation. "
                             + "Not validated for regulatory submissions."
            };
            _db.ModelRegistrations.Add(model);
        }

        // 5. Prediction job + result (Completed)
        var job = new PredictionJob
        {
            VersionId = version.Id,
            Status = JobStatus.Completed,
            Progress = 100,
            Stage = "Completed",
            RequestedBy = request.UserId,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(-4)
        };
        _db.PredictionJobs.Add(job);

        var result = new PredictionResult
        {
            JobId = job.Id,
            ModelRegistrationId = model.Id,
            SuccessProbability = 0.78,
            ToxicityScore = 0.30,
            StabilityScore = 0.85,
            SideRiskLevel = SideRiskLevel.Medium,
            Summary = "Standard IR aspirin blend with good manufacturability outlook. "
                    + "Hydrolysis risk at high humidity is the primary concern — "
                    + "consider adding a desiccant in packaging.",
            RationaleItems = new List<RationaleItem>
            {
                new() { Category = RationaleCategory.Synthesis,
                    Claim = "Blend feasibility is high for a direct compression route.",
                    Explanation = "All four excipients are commercially available in compendial grades "
                                + "and are commonly used together in immediate-release tablets.",
                    Confidence = 0.88,
                    SourcesJson = JsonSerializer.Serialize(new[] {
                        new { title = "Handbook of Pharmaceutical Excipients", reference = "8th ed., 2020",
                              type = "reference", similarity = 0.0 }
                    }) },
                new() { Category = RationaleCategory.Stability,
                    Claim = "Aspirin is susceptible to hydrolytic degradation at high humidity.",
                    Explanation = "Acetylsalicylic acid undergoes hydrolysis to salicylic acid and acetic "
                                + "acid in presence of moisture; rate increases with temperature (Arrhenius).",
                    Confidence = 0.92,
                    SourcesJson = JsonSerializer.Serialize(new[] {
                        new { title = "Stability of Aspirin Tablets", reference = "J Pharm Sci, 2019",
                              type = "paper", similarity = 0.0 }
                    }) },
                new() { Category = RationaleCategory.Toxicity,
                    Claim = "Low acute toxicity at therapeutic doses; GI irritation is dose-dependent.",
                    Explanation = "The NSAID class carries known GI and bleeding risk; 500 mg dose "
                                + "is within established safe limits for short-term use.",
                    Confidence = 0.80,
                    SourcesJson = JsonSerializer.Serialize(new[] {
                        new { title = "Aspirin Drug Label", reference = "FDA, 2021",
                              type = "regulatory", similarity = 0.0 }
                    }) },
                new() { Category = RationaleCategory.Gap,
                    Claim = "Dissolution profile not yet tested experimentally.",
                    Explanation = "Predicted disintegration time is estimated from excipient ratios; "
                                + "experimental dissolution data is needed for confidence.",
                    Confidence = 0.55,
                    SourcesJson = "[]" },
            }
        };
        _db.PredictionResults.Add(result);

        // 6. Review
        var review = new PredictionReview
        {
            ResultId = result.Id,
            ReviewerUserId = request.UserId,
            Decision = ReviewDecision.Approved,
            Comment = "Approved for prototype batch. Proceed with 100-tablet compression trial."
        };
        _db.PredictionReviews.Add(review);

        // 7. Lab outcome
        var outcome = new ExperimentOutcome
        {
            ResultId = result.Id,
            ActualSuccess = true,
            ActualMetricsJson = JsonSerializer.Serialize(new
            {
                hardnessN = 72,
                disintegrationMin = 5.4,
                dissolutionAt30Min = 0.93,
                assayPercent = 99.6,
                friabilityPercent = 0.42
            }),
            Notes = "Batch #001: 100 tablets, 500 mg each. All in-spec except slightly high friability — "
                  + "consider extragranular MCC for next batch.",
            RecordedBy = request.UserId
        };
        _db.ExperimentOutcomes.Add(outcome);

        // 8. Simulation job + result (5 кандидатов)
        var simJob = new SimulationJob
        {
            VersionId = version.Id,
            ConfigJson = JsonSerializer.Serialize(new
            {
                iterations = 25,
                varyConcentrations = true,
                varyTemperature = true,
                varyPh = false,
                seed = 42,
                targetMetric = "success"
            }),
            Status = JobStatus.Completed,
            Progress = 100,
            RequestedBy = request.UserId,
            StartedAtUtc = DateTime.UtcNow.AddHours(-1),
            CompletedAtUtc = DateTime.UtcNow.AddHours(-1).AddMinutes(3)
        };
        _db.SimulationJobs.Add(simJob);

        var simResult = new SimulationResult
        {
            JobId = simJob.Id,
            Summary = "Across 25 virtual candidates, the strongest driver of success is the "
                    + "disintegrant-to-binder ratio. Peak predicted success at ~10% starch + 18% MCC.",
            IterationsExecuted = 25,
            Candidates = new List<SimulationCandidate>()
        };

        var candidates = new (double Starch, double Mcc, double Success, double Score)[]
        {
            (0.10, 0.18, 0.84, 0.82),
            (0.08, 0.20, 0.78, 0.76),
            (0.12, 0.16, 0.76, 0.73),
            (0.06, 0.22, 0.72, 0.70),
            (0.14, 0.14, 0.70, 0.67),
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            var c = candidates[i];
            var paramObj = new
            {
                aspirinProportion = 0.70,
                starchProportion = c.Starch,
                mccProportion = c.Mcc,
                magnesiumStearateProportion = 0.02,
                temperatureC = 25 + i * 1.5,
                phTarget = 6.8
            };
            simResult.Candidates.Add(new SimulationCandidate
            {
                ParametersJson = JsonSerializer.Serialize(paramObj),
                SuccessProbability = c.Success,
                Score = c.Score,
                Rank = i + 1
            });
        }

        _db.SimulationResults.Add(simResult);

        // 9. Исследование стабильности (3T × 4t, k25 = 0.0005/day, Ea = 80 kJ/mol)
        var stability = new StabilityStudy
        {
            VersionId = version.Id,
            Notes = "Accelerated stability: 25, 40, 50 °C, 0/30/60/90 days. "
                  + "HPLC assay, n=3 per timepoint.",
            CreatedBy = request.UserId,
            Points = new List<StabilityPoint>()
        };

        (double TempC, double[] Assays)[] stabilityData = new[]
        {
            (25.0, new [] { 100.0, 98.51, 97.04, 95.60 }),
            (40.0, new [] { 100.0, 93.18, 86.82, 80.90 }),
            (50.0, new [] { 100.0, 83.42, 69.59, 58.06 }),
        };
        int[] days = { 0, 30, 60, 90 };

        foreach (var (temp, assays) in stabilityData)
        for (int i = 0; i < days.Length; i++)
        {
            stability.Points.Add(new StabilityPoint
            {
                TemperatureCelsius = temp,
                TimeDays = days[i],
                AssayPercent = assays[i]
            });
        }
        // EnsureValid не вызываем — данные известные и корректные.
        _db.StabilityStudies.Add(stability);

        // 10. Документы в Knowledge Base.
        // Чанки с эмбеддингами не создаём: фейковые векторы загрязнили бы семантический поиск
        // (нулевой вектор ломает cosine distance, случайный даёт ложные совпадения), а реальная
        // эмбеддинг-генерация требует внешнего AI. Документы видны в списке KB и показывают
        // онбординговую ценность; пользователь может индексировать собственный контент.
        var kbDoc1 = new KnowledgeDocument
        {
            ProjectId = project.Id,
            Title = "Aspirin Stability Review (demo)",
            SourceType = SourceType.Paper,
            Reference = "Demo Ref: J Pharm Sci, 2019, 108(5)",
            Status = "Ready",
            UploadedBy = request.UserId
        };
        var kbDoc2 = new KnowledgeDocument
        {
            ProjectId = project.Id,
            Title = "Tablet Excipient Compatibility Guide (demo)",
            SourceType = SourceType.InternalExperiment,
            Reference = "Demo Ref: Internal R&D Note RD-2024-017",
            Status = "Ready",
            UploadedBy = request.UserId
        };
        _db.KnowledgeDocuments.Add(kbDoc1);
        _db.KnowledgeDocuments.Add(kbDoc2);

        await _db.SaveChangesAsync(ct);

        return new DemoDataDto(project.Id, formulation.Id, 1);
    }
}
