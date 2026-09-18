using MediatR;

namespace Experimento.Application.Features.Audit;

public record GetAuditTrailQuery(string? EntityType, string? EntityId, int Skip = 0, int Take = 50) : IRequest<IReadOnlyList<AuditEntryDto>>;
public record VerifyAuditIntegrityQuery : IRequest<(bool IsIntact, long? FirstBrokenId)>;

public class GetAuditTrailHandler : IRequestHandler<GetAuditTrailQuery, IReadOnlyList<AuditEntryDto>>
{
    private readonly IAuditTrail _audit;
    public GetAuditTrailHandler(IAuditTrail audit) => _audit = audit;

    public async Task<IReadOnlyList<AuditEntryDto>> Handle(GetAuditTrailQuery request, CancellationToken ct)
    {
        // Жёсткие границы: один запрос не должен вытаскивать неограниченный объём журнала.
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take <= 0 ? 50 : request.Take, 1, 200);
        var entries = await _audit.GetTrailAsync(request.EntityType, request.EntityId, skip, take, ct);
        return entries.Select(e => new AuditEntryDto(e.Id, e.TimestampUtc, e.ActorUserId, e.Action, e.EntityType, e.EntityId)).ToList();
    }
}

public class VerifyAuditIntegrityHandler : IRequestHandler<VerifyAuditIntegrityQuery, (bool IsIntact, long? FirstBrokenId)>
{
    private readonly IAuditTrail _audit;
    public VerifyAuditIntegrityHandler(IAuditTrail audit) => _audit = audit;

    public async Task<(bool IsIntact, long? FirstBrokenId)> Handle(VerifyAuditIntegrityQuery request, CancellationToken ct)
    {
        var broken = await _audit.VerifyChainAsync(ct);
        return (broken is null, broken);
    }
}
