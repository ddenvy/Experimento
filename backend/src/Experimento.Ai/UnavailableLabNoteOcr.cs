using Experimento.Application.Abstractions;

namespace Experimento.Ai;

/// <summary>
/// Заглушка OCR для провайдеров без поддержки изображений (openai-совместимые, ollama, none).
/// Сообщает об отсутствии возможности явно, чтобы пользователь получил понятную ошибку,
/// а не пустой документ.
/// </summary>
public class UnavailableLabNoteOcr : ILabNoteOcr
{
    public Task<string> TranscribeAsync(byte[] image, string contentType, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Image OCR is not available: it requires the 'gemini' AI provider (multimodal vision).");
}
