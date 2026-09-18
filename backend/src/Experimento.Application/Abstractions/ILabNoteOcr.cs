namespace Experimento.Application.Abstractions;

/// <summary>
/// Распознавание заметок лабораторного журнала по фотографии или скану страницы.
/// Реализуется мультимодальной моделью: текст транскрибируется и размечается по смыслу,
/// чтобы попасть в общий пайплайн чанкинга и семантического поиска.
/// </summary>
public interface ILabNoteOcr
{
    /// <summary>
    /// Транскрибирует изображение страницы. Возвращает пустую строку, если
    /// читаемого лабораторного содержимого на изображении нет.
    /// Бросает <see cref="NotSupportedException"/>, если OCR недоступен для текущего AI-провайдера.
    /// </summary>
    Task<string> TranscribeAsync(byte[] image, string contentType, CancellationToken cancellationToken = default);
}
