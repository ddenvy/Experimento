using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Experimento.Ai;

/// <summary>
/// Generates structured rationale items with source attribution for a prediction.
/// Uses Semantic Kernel chat completion with knowledge/formulation/history plugins.
/// </summary>
public class RationaleGenerator : IRationaleGenerator
{
    private readonly IChatCompletionService _chat;
    private readonly IVectorSearchService _search;
    private readonly IAppDbContext _db;
    private readonly ILogger<RationaleGenerator> _logger;

    public RationaleGenerator(IChatCompletionService chat, IVectorSearchService search, IAppDbContext db, ILogger<RationaleGenerator> logger)
    {
        _chat = chat;
        _search = search;
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RationaleItem>> GenerateAsync(
        FormulationSnapshot snapshot, PredictionOutcome outcome, Guid resultId,
        CancellationToken cancellationToken = default)
    {
        var items = new List<RationaleItem>();

        // 1. Build factor-based rationale from heuristic scoring (always available, no LLM needed).
        foreach (var factor in outcome.Factors)
        {
            var category = factor.Name switch
            {
                "ToxicophorePresence" => RationaleCategory.Toxicity,
                "StabilizerForDelivery" or "TemperatureStability" or "ProportionUniformity" => RationaleCategory.Stability,
                _ => RationaleCategory.Synthesis
            };

            var sources = await FindSourcesAsync(factor.Description, snapshot, cancellationToken);
            items.Add(new RationaleItem
            {
                ResultId = resultId,
                Category = category,
                Claim = $"{factor.Name}: contribution {(factor.Contribution >= 0 ? "+" : "")}{factor.Contribution:0.00}",
                Explanation = factor.Description,
                Confidence = Math.Clamp(0.5 + factor.Contribution, 0.1, 0.99),
                SourcesJson = JsonSerializer.Serialize(sources)
            });
        }

        // 2. Gap detection: if success probability is low, flag missing components.
        if (outcome.SuccessProbability < 0.6)
        {
            var missing = new List<string>();
            if (!string.IsNullOrEmpty(snapshot.Conditions.DeliveryTarget) &&
                !snapshot.Components.Any(c => string.Equals(c.Role, "stabilizer", StringComparison.OrdinalIgnoreCase)))
                missing.Add("stabilizer for targeted delivery");
            if (snapshot.Components.Count < 2)
                missing.Add("additional components for formulation balance");

            if (missing.Count > 0)
            {
                items.Add(new RationaleItem
                {
                    ResultId = resultId,
                    Category = RationaleCategory.Gap,
                    Claim = "Low success probability: missing key components",
                    Explanation = $"Consider adding: {string.Join(", ", missing)}.",
                    Confidence = 0.7,
                    SourcesJson = "[]"
                });
            }
        }

        // 3. If an LLM is configured, enrich the factor explanations with a narrative.
        if (_chat is not FallbackChatCompletionService)
        {
            try
            {
                await EnrichWithLlmAsync(snapshot, outcome, items, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM rationale enrichment failed; keeping heuristic items only.");
            }
        }

        return items;
    }

    private async Task<List<RationaleSourceDto>> FindSourcesAsync(string query, FormulationSnapshot snapshot,
        CancellationToken ct)
    {
        // Rationale формируется в системном контексте: источники — только общедоступная
        // глобальная база знаний, без приватных документов проектов других пользователей.
        var results = await _search.SearchAsync(query, topK: 3, projectId: null, userId: Guid.Empty, ct);
        return results.Select(r => new RationaleSourceDto(
            r.Document.Title, r.Document.Reference, r.Document.SourceType.ToString(), r.Similarity)).ToList();
    }

    private async Task EnrichWithLlmAsync(FormulationSnapshot snapshot, PredictionOutcome outcome,
        List<RationaleItem> items, CancellationToken ct)
    {
        var history = new ChatHistory();
        history.AddSystemMessage("You are a formulation science assistant. Explain prediction factors concisely.");
        var prompt = $"Formulation target: {snapshot.TargetPurpose}. " +
                     $"Predicted success: {outcome.SuccessProbability:P1}. " +
                     $"Factors: {string.Join("; ", outcome.Factors.Select(f => $"{f.Name}={f.Contribution:+.2f}: {f.Description}"))}. " +
                     $"Provide a short mechanistic explanation.";
        history.AddUserMessage(prompt);

        var response = await _chat.GetChatMessageContentAsync(history, cancellationToken: ct);
        var text = response.Content ?? string.Empty;

        // Append an LLM-enriched synthesis rationale item.
        if (!string.IsNullOrWhiteSpace(text))
        {
            items.Add(new RationaleItem
            {
                ResultId = items.FirstOrDefault()?.ResultId ?? Guid.Empty,
                Category = RationaleCategory.Synthesis,
                Claim = "LLM mechanistic summary",
                Explanation = text,
                Confidence = 0.6,
                SourcesJson = "[]"
            });
        }
    }
}
