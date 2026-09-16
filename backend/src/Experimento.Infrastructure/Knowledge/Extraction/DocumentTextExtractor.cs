using Experimento.Application.Abstractions;

namespace Experimento.Infrastructure.Knowledge.Extraction;

/// <summary>
/// Маршрутизирует файл в нужный экстрактор по расширению.
/// Файл читается в память полностью (лимит 10 МБ контролируется на уровне контроллера).
/// </summary>
public class DocumentTextExtractor : IDocumentTextExtractor
{
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md",
        ".csv",
        ".xls", ".xlsx",
        ".docx",
        ".pdf"
    };

    public async Task<string> ExtractAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName);
        if (!SupportedExtensions.Contains(extension))
            throw new ArgumentException($"Unsupported file type '{extension}'. Supported: {string.Join(", ", SupportedExtensions)}.");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        var text = extension switch
        {
            ".txt" or ".md" => PlainTextExtractor.Extract(bytes),
            ".csv" => CsvTableExtractor.Extract(bytes),
            ".xls" or ".xlsx" => SpreadsheetExtractor.Extract(new MemoryStream(bytes, writable: false)),
            ".docx" => DocxTextExtractor.Extract(new MemoryStream(bytes, writable: false)),
            ".pdf" => PdfTextExtractor.Extract(new MemoryStream(bytes, writable: false)),
            _ => throw new ArgumentException($"Unsupported file type '{extension}'.")
        };

        return text.Trim();
    }
}
