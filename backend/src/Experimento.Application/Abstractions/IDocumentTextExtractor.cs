namespace Experimento.Application.Abstractions;

/// <summary>
/// Извлекает plain-text из загруженного файла документа (PDF, DOCX, TXT, CSV, XLS, XLSX).
/// Табличные форматы преобразуются в строки "Заголовок: значение", пригодные для
/// эмбеддинга и семантического поиска.
/// </summary>
public interface IDocumentTextExtractor
{
    /// <summary>Поддерживаемые расширения файлов (в нижнем регистре, с точкой).</summary>
    IReadOnlySet<string> SupportedExtensions { get; }

    /// <summary>Извлекает текст. Бросает ArgumentException для неподдерживаемого формата.</summary>
    Task<string> ExtractAsync(string fileName, Stream content, CancellationToken cancellationToken = default);
}
