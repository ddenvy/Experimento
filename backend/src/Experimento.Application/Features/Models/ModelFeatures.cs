using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Experimento.Application.Features.Models;

public record ListModelsQuery : IRequest<IReadOnlyList<ModelRegistrationDto>>;

public class ListModelsHandler : IRequestHandler<ListModelsQuery, IReadOnlyList<ModelRegistrationDto>>
{
    private readonly IAppDbContext _db;
    public ListModelsHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ModelRegistrationDto>> Handle(ListModelsQuery request, CancellationToken ct)
    {
        return await _db.ModelRegistrations
            .OrderBy(m => m.RegisteredAtUtc)
            .Select(m => new ModelRegistrationDto(m.Id, m.Name, m.Version, m.Description, m.ContextOfUse, m.RegisteredAtUtc))
            .ToListAsync(ct);
    }
}
