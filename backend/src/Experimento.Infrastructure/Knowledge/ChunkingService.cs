namespace Experimento.Infrastructure.Knowledge;

/// <summary>
/// Splits document text into overlapping chunks for embedding.
/// </summary>
public class ChunkingService
{
    public IReadOnlyList<string> Chunk(string content, int chunkSizeTokens = 800, int overlapTokens = 100)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<string>();

        // Rough token estimate: ~4 chars per token.
        var chunkSizeChars = chunkSizeTokens * 4;
        var overlapChars = overlapTokens * 4;

        var chunks = new List<string>();
        for (int i = 0; i < content.Length; i += chunkSizeChars - overlapChars)
        {
            var len = Math.Min(chunkSizeChars, content.Length - i);
            chunks.Add(content.Substring(i, len).Trim());
            if (i + len >= content.Length) break;
        }
        return chunks;
    }
}
