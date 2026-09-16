using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Experimento.Application.Features.Knowledge;

public record UploadDocumentCommand(Guid? ProjectId, string Title, string SourceType, string Reference, string Content, Guid UploadedBy) : IRequest<KnowledgeDocumentDto>;
public record ListDocumentsQuery(Guid? ProjectId, Guid UserId = default) : IRequest<IReadOnlyList<KnowledgeDocumentDto>>;
public record SearchKnowledgeQuery(string Query, Guid? ProjectId, int TopK = 5, Guid UserId = default) : IRequest<IReadOnlyList<SearchResultDto>>;

public class UploadDocumentValidator : AbstractValidator<UploadDocumentCommand>
{
    public UploadDocumentValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.SourceType).NotEmpty();
        RuleFor(x => x.Reference).MaximumLength(1000);
        // Ограничение размера: защита от неограничённого чанкинга и эмбеддинга.
        RuleFor(x => x.Content).NotEmpty().MaximumLength(200_000);
    }
}

public class SearchKnowledgeValidator : AbstractValidator<SearchKnowledgeQuery>
{
    public SearchKnowledgeValidator()
    {
        RuleFor(x => x.Query).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.TopK).InclusiveBetween(1, 50);
    }
}

public class UploadDocumentHandler : IRequestHandler<UploadDocumentCommand, KnowledgeDocumentDto>
{
    private readonly IAppDbContext _db;
    private readonly IPublishEndpoint _publish;
    private readonly ResourceAuthorization _auth;
    public UploadDocumentHandler(IAppDbContext db, IPublishEndpoint publish, ResourceAuthorization auth)
        => (_db, _publish, _auth) = (db, publish, auth);

    public async Task<KnowledgeDocumentDto> Handle(UploadDocumentCommand request, CancellationToken ct)
    {
        if (!Enum.TryParse<SourceType>(request.SourceType, true, out var sourceType))
            throw new BadRequestException($"Invalid source type: {request.SourceType}");
        if (request.ProjectId is Guid pid && !await _auth.OwnsProjectAsync(pid, request.UploadedBy, ct))
            throw new ForbiddenException();

        var doc = new KnowledgeDocument
        {
            ProjectId = request.ProjectId,
            Title = request.Title,
            SourceType = sourceType,
            Reference = request.Reference,
            UploadedBy = request.UploadedBy,
            Status = "Pending"
        };
        _db.KnowledgeDocuments.Add(doc);
        await _db.SaveChangesAsync(ct);

        // Чанкинг и эмбеддинги строит консьюмер; сырой контент передаём в сообщении,
        // чтобы не вставлять временную строку с пустым вектором (vector(1536) NOT NULL).
        await _publish.Publish(new Messaging.IngestDocumentCommand(doc.Id, request.Content), ct);

        return new KnowledgeDocumentDto(doc.Id, doc.Title, doc.SourceType.ToString(), doc.Reference, doc.Status, doc.UploadedAtUtc);
    }
}

public class ListDocumentsHandler : IRequestHandler<ListDocumentsQuery, IReadOnlyList<KnowledgeDocumentDto>>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public ListDocumentsHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<IReadOnlyList<KnowledgeDocumentDto>> Handle(ListDocumentsQuery request, CancellationToken ct)
    {
        var query = _db.KnowledgeDocuments.AsQueryable();

        if (request.ProjectId is Guid pid)
        {
            // Документы конкретного проекта видны только его владельцу.
            if (!await _auth.OwnsProjectAsync(pid, request.UserId, ct))
                throw new ForbiddenException();
            query = query.Where(d => d.ProjectId == pid);
        }
        else
        {
            // Без проекта: глобальная база + собственные загрузки + документы своих проектов.
            query = query.Where(d =>
                d.ProjectId == null ||
                d.UploadedBy == request.UserId ||
                _db.Projects.Any(p => p.Id == d.ProjectId && p.CreatedBy == request.UserId));
        }

        return await query.Select(d => new KnowledgeDocumentDto(d.Id, d.Title, d.SourceType.ToString(),
            d.Reference, d.Status, d.UploadedAtUtc)).ToListAsync(ct);
    }
}

public class SearchKnowledgeHandler : IRequestHandler<SearchKnowledgeQuery, IReadOnlyList<SearchResultDto>>
{
    private readonly IVectorSearchService _search;
    public SearchKnowledgeHandler(IVectorSearchService search) => _search = search;

    public async Task<IReadOnlyList<SearchResultDto>> Handle(SearchKnowledgeQuery request, CancellationToken ct)
    {
        var results = await _search.SearchAsync(request.Query, request.TopK, request.ProjectId, request.UserId, ct);
        return results.Select(r => new SearchResultDto(r.Chunk.Id, r.Document.Title, r.Document.Reference,
            r.Document.SourceType.ToString(), r.Chunk.Content, r.Similarity)).ToList();
    }
}
