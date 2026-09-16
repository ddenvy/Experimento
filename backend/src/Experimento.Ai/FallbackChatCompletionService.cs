using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Experimento.Ai;

/// <summary>
/// Fallback chat completion used when no LLM provider is configured.
/// Returns a deterministic response indicating LLM is unavailable.
/// </summary>
public class FallbackChatCompletionService : IChatCompletionService
{
    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        var message = new ChatMessageContent(AuthorRole.Assistant,
            "[LLM not configured] Rationale derived from heuristic scoring factors only.");
        return Task.FromResult<IReadOnlyList<ChatMessageContent>>(new[] { message });
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        var message = new StreamingChatMessageContent(AuthorRole.Assistant,
            "[LLM not configured] Rationale derived from heuristic scoring factors only.");
        yield return message;
        await Task.CompletedTask;
    }
}
