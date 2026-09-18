using Experimento.Application.Abstractions;

namespace Experimento.Infrastructure.Knowledge.Extraction;

/// <summary>
/// Маршрутизирует файл в нужный экстрактор по расширению.
/// Текстовые и табличные форматы разбираются локально, изображения страниц
/// лабораторного журнала распознаются мультимодальной моделью (<see cref="ILabNoteOcr"/>).
/// Файл читается в память полностью (лимит 10 МБ контролируется на уровне контроллера).
/// </summary>
public class DocumentTextExtractor : IDocumentTextExtractor
{
    /// <summary>Расширения изображений и MIME-типы, которые принимает мультимодальная модель.</summary>
    private static readonly Dictionary<string, string> ImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
    };

    private readonly ILabNoteOcr _ocr;
    public DocumentTextExtractor(ILabNoteOcr ocr) => _ocr = ocr;

    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(
        new[] { ".txt", ".md", ".csv", ".xls", ".xlsx", ".docx", ".pdf" }.Concat(ImageContentTypes.Keys),
        StringComparer.OrdinalIgnoreCase);

    public async Task<string> ExtractAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName);
        if (!SupportedExtensions.Contains(extension))
            throw new ArgumentException($"Unsupported file type '{extension}'. Supported: {string.Join(", ", SupportedExtensions)}.");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        // Изображения не парсятся локально — их читает модель (OCR).
        if (ImageContentTypes.TryGetValue(extension, out var contentType))
            return (await _ocr.TranscribeAsync(bytes, contentType, cancellationToken)).Trim();

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
