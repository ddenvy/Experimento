using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Experimento.Application.Features.Projects;

public record CreateProjectCommand(string Name, string? Description, Guid CreatedBy) : IRequest<ProjectDto>;
public record UpdateProjectCommand(Guid Id, string Name, string? Description, Guid UserId = default) : IRequest<ProjectDto>;
public record DeleteProjectCommand(Guid Id, Guid UserId = default) : IRequest<Unit>;
public record GetProjectQuery(Guid Id, Guid UserId = default) : IRequest<ProjectDto>;
public record ListProjectsQuery(Guid? UserId) : IRequest<IReadOnlyList<ProjectDto>>;

public class CreateProjectHandler : IRequestHandler<CreateProjectCommand, ProjectDto>
{
    private readonly IAppDbContext _db;
    public CreateProjectHandler(IAppDbContext db) => _db = db;

    public async Task<ProjectDto> Handle(CreateProjectCommand request, CancellationToken ct)
    {
        var project = new Project { Name = request.Name, Description = request.Description, CreatedBy = request.CreatedBy };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);
        return new ProjectDto(project.Id, project.Name, project.Description, project.CreatedAtUtc);
    }
}

public class UpdateProjectHandler : IRequestHandler<UpdateProjectCommand, ProjectDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public UpdateProjectHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<ProjectDto> Handle(UpdateProjectCommand request, CancellationToken ct)
    {
        var project = await _db.Projects.FindAsync([request.Id], ct)
                      ?? throw new NotFoundException($"Project {request.Id} not found.");
        if (!await _auth.OwnsProjectAsync(request.Id, request.UserId, ct))
            throw new ForbiddenException();

        project.Name = request.Name;
        project.Description = request.Description;
        await _db.SaveChangesAsync(ct);
        return new ProjectDto(project.Id, project.Name, project.Description, project.CreatedAtUtc);
    }
}

public class DeleteProjectHandler : IRequestHandler<DeleteProjectCommand, Unit>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public DeleteProjectHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<Unit> Handle(DeleteProjectCommand request, CancellationToken ct)
    {
        var project = await _db.Projects.FindAsync([request.Id], ct)
                      ?? throw new NotFoundException($"Project {request.Id} not found.");
        if (!await _auth.OwnsProjectAsync(request.Id, request.UserId, ct))
            throw new ForbiddenException();

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public class GetProjectHandler : IRequestHandler<GetProjectQuery, ProjectDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public GetProjectHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<ProjectDto> Handle(GetProjectQuery request, CancellationToken ct)
    {
        var p = await _db.Projects.FindAsync([request.Id], ct)
                ?? throw new NotFoundException($"Project {request.Id} not found.");
        if (!await _auth.OwnsProjectAsync(request.Id, request.UserId, ct))
            throw new ForbiddenException();

        return new ProjectDto(p.Id, p.Name, p.Description, p.CreatedAtUtc);
    }
}

public class ListProjectsHandler : IRequestHandler<ListProjectsQuery, IReadOnlyList<ProjectDto>>
{
    private readonly IAppDbContext _db;
    public ListProjectsHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ProjectDto>> Handle(ListProjectsQuery request, CancellationToken ct)
    {
        var query = _db.Projects.AsQueryable();
        if (request.UserId.HasValue)
            query = query.Where(p => p.CreatedBy == request.UserId.Value);
        return await query.Select(p => new ProjectDto(p.Id, p.Name, p.Description, p.CreatedAtUtc))
            .ToListAsync(ct);
    }
}
