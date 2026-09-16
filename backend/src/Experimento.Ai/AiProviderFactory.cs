using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;

namespace Experimento.Ai;

/// <summary>
/// Registers AI services based on the configured provider.
/// Supports: openai, azureopenai, ollama, gemini, none.
/// With provider=none the system runs fully offline: heuristic predictions still work,
/// rationale items are marked as "LLM not configured".
/// </summary>
public static class AiProviderFactory
{
    // Один общий HttpClient на время жизни приложения (защита от исчерпания сокетов).
    private static readonly System.Net.Http.HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public static IServiceCollection AddAiServices(this IServiceCollection services, IConfiguration config)
    {
        var provider = (config["Ai:Provider"] ?? "none").ToLowerInvariant();

        IChatCompletionService? chatService = null;
        ITextEmbeddingGenerationService? embeddingService = null;

        switch (provider)
        {
            case "openai":
                var apiKey = config["Ai:OpenAi:ApiKey"];
                var chatModel = config["Ai:OpenAi:ChatModel"] ?? "gpt-4o-mini";
                var embeddingModel = config["Ai:OpenAi:EmbeddingModel"] ?? "text-embedding-3-small";
                if (!string.IsNullOrEmpty(apiKey))
                {
                    var ob = Kernel.CreateBuilder();
                    ob.AddOpenAIChatCompletion(chatModel, apiKey);
                    ob.AddOpenAITextEmbeddingGeneration(embeddingModel, apiKey);
                    var ok = ob.Build();
                    chatService = ok.Services.GetService<IChatCompletionService>();
                    embeddingService = ok.Services.GetService<ITextEmbeddingGenerationService>();
                }
                break;

            case "azureopenai":
                var azureEndpoint = config["Ai:AzureOpenAi:Endpoint"];
                var azureKey = config["Ai:AzureOpenAi:ApiKey"];
                var azureDeployment = config["Ai:AzureOpenAi:ChatDeployment"] ?? "gpt-4o-mini";
                var azureEmbedding = config["Ai:AzureOpenAi:EmbeddingDeployment"] ?? "text-embedding-3-small";
                if (!string.IsNullOrEmpty(azureEndpoint) && !string.IsNullOrEmpty(azureKey))
                {
                    var ab = Kernel.CreateBuilder();
                    ab.AddAzureOpenAIChatCompletion(azureDeployment, azureEndpoint, azureKey);
                    ab.AddAzureOpenAITextEmbeddingGeneration(azureEmbedding, azureEndpoint, azureKey);
                    var ak = ab.Build();
                    chatService = ak.Services.GetService<IChatCompletionService>();
                    embeddingService = ak.Services.GetService<ITextEmbeddingGenerationService>();
                }
                break;

            case "ollama":
                var ollamaEndpoint = config["Ai:Ollama:Endpoint"] ?? "http://localhost:11434";
                var ollamaModel = config["Ai:Ollama:Model"] ?? "llama3";
                var ollamaClient = new System.Net.Http.HttpClient
                {
                    BaseAddress = new Uri(ollamaEndpoint),
                    Timeout = TimeSpan.FromSeconds(60)
                };
                var ollamaBuilder = Kernel.CreateBuilder();
                ollamaBuilder.AddOpenAIChatCompletion(ollamaModel, apiKey: "ollama", httpClient: ollamaClient);
                var ollamaKernel = ollamaBuilder.Build();
                chatService = ollamaKernel.Services.GetService<IChatCompletionService>();
                break;

            case "gemini":
                var geminiKey = config["Ai:Gemini:ApiKey"];
                var geminiModel = (config["Ai:Gemini:ChatModel"] ?? "gemini-3.5-flash-lite").Replace("models/", "");
                // Эмбеддинги доступны только через API v1 (модели gemini-embedding-*).
                var geminiEmbeddingModel = (config["Ai:Gemini:EmbeddingModel"] ?? "gemini-embedding-001").Replace("models/", "");
                if (!string.IsNullOrEmpty(geminiKey))
                {
                    chatService = new DirectGeminiChatCompletionService(SharedHttpClient, geminiModel, geminiKey);
                    embeddingService = new DirectGeminiEmbeddingGenerationService(SharedHttpClient, geminiEmbeddingModel, geminiKey);
                }
                break;
        }

        services.AddSingleton(chatService ?? new FallbackChatCompletionService());
        services.AddSingleton<IEmbeddingService>(sp =>
            new EmbeddingService(embeddingService, provider, sp.GetService<Microsoft.Extensions.Logging.ILogger<EmbeddingService>>()));

        services.AddScoped<IRationaleGenerator, RationaleGenerator>();

        return services;
    }
}
