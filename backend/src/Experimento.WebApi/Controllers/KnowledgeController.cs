using Experimento.Application.Features.Knowledge;
using Experimento.Application.Abstractions;
using Experimento.Application.Exceptions;
using Experimento.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Experimento.WebApi.Controllers;

public class KnowledgeController : BaseController
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;
    private const int MaxExtractedChars = 200_000;

    private readonly IDocumentTextExtractor _extractor;

    public KnowledgeController(IMediator mediator, ICurrentUser currentUser, IAuditTrail audit,
        IDocumentTextExtractor extractor)
        : base(mediator, currentUser, audit)
        => _extractor = extractor;

    [HttpPost("documents")]
    public async Task<IActionResult> Upload([FromBody] UploadDocumentCommand cmd)
    {
        var result = await Mediator.Send(cmd with { UploadedBy = UserId });
        // В журнал нельзя класть сам Content (до 200 000 символов) — только метаданные.
        await AuditAsync("Knowledge.Upload", "KnowledgeDocument", result.Id.ToString(),
            new { cmd.Title, cmd.SourceType, cmd.Reference, cmd.ProjectId, contentLength = cmd.Content.Length });
        return Ok(result);
    }

    /// <summary>
    /// Загрузка файла документа: PDF, DOCX, TXT, MD, CSV, XLS, XLSX, а также фотографии
    /// и сканы страниц лабораторного журнала (PNG, JPEG, WEBP) — до 10 МБ.
    /// Текст извлекается на сервере (изображения распознаются мультимодальной моделью,
    /// таблицы — построчно "Заголовок: значение") и далее проходит общий пайплайн
    /// чанкинга и эмбеддинга.
    /// </summary>
    [HttpPost("documents/upload")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> UploadFile(
        IFormFile? file,
        [FromForm] Guid? projectId,
        [FromForm] string? sourceType,
        [FromForm] string? reference,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            throw new BadRequestException("File is required.");
        if (file.Length > MaxUploadBytes)
            throw new BadRequestException("File must be 10 MB or smaller.");

        var fileName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(fileName);
        if (!_extractor.SupportedExtensions.Contains(extension))
            throw new BadRequestException(
                $"Unsupported file type '{extension}'. Supported: {string.Join(", ", _extractor.SupportedExtensions)}.");

        string content;
        try
        {
            await using var stream = file.OpenReadStream();
            content = await _extractor.ExtractAsync(fileName, stream, cancellationToken);
        }
        catch (NotSupportedException ex)
        {
            // Например, распознавание изображений недоступно на текущем AI-провайдере.
            throw new BadRequestException(ex.Message);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            throw new BadRequestException($"Could not parse the file: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(content))
            throw new BadRequestException(
                "No text could be extracted from the file. Images must contain legible laboratory notes; "
                + "scanned PDFs without a text layer are not supported.");
        if (content.Length > MaxExtractedChars)
            throw new BadRequestException($"Extracted text is too long: {content.Length}/{MaxExtractedChars} characters.");

        var cmd = new UploadDocumentCommand(
            projectId,
            Title: Path.GetFileNameWithoutExtension(fileName),
            SourceType: string.IsNullOrWhiteSpace(sourceType) ? "InternalExperiment" : sourceType,
            Reference: reference ?? fileName,
            Content: content,
            UploadedBy: UserId);

        var result = await Mediator.Send(cmd, cancellationToken);
        await AuditAsync("Knowledge.UploadFile", "KnowledgeDocument", result.Id.ToString(),
            new { fileName, file.Length, cmd.SourceType });
        return Ok(result);
    }

    [HttpGet("documents")]
    public async Task<IActionResult> List([FromQuery] Guid? projectId)
        => Ok(await Mediator.Send(new ListDocumentsQuery(projectId, UserId)));

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SearchKnowledgeQuery query)
        => Ok(await Mediator.Send(query with { UserId = UserId }));
}
