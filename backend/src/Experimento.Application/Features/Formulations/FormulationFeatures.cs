using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Experimento.Application.Features.Formulations;

public record ComponentInput(string ChemicalName, string? CasNumber, string? Formula, double MolarMass, double Proportion, string? Role, int? PubChemCid = null);
public record ConditionsInput(double TemperatureCelsius, double? PressureKPa, double? PhTarget, string? Solvent, string? DeliveryTarget);

public record CreateFormulationCommand(Guid ProjectId, string Name, string TargetPurpose, Guid UserId = default) : IRequest<FormulationDto>;
public record CreateVersionCommand(
    Guid FormulationId,
    IReadOnlyList<ComponentInput> Components,
    ConditionsInput Conditions,
    string? Notes,
    Guid CreatedBy) : IRequest<FormulationVersionDto>;
public record ListVersionsQuery(Guid FormulationId, Guid UserId = default) : IRequest<IReadOnlyList<FormulationVersionDto>>;
public record ListFormulationsByProjectQuery(Guid ProjectId, Guid UserId = default) : IRequest<IReadOnlyList<FormulationDto>>;
public record GetVersionQuery(Guid VersionId, Guid UserId = default) : IRequest<FormulationVersionDto>;
public record CompareVersionsQuery(Guid FormulationId, Guid VersionAId, Guid VersionBId, Guid UserId = default) : IRequest<VersionComparisonDto>;

public record ComponentDiff(
    string ChemicalName, string Change, // "Added", "Removed", "Modified"
    double? ProportionA, double? ProportionB, double? MolarMassA, double? MolarMassB);
public record VersionComparisonDto(
    FormulationVersionDto VersionA, FormulationVersionDto VersionB,
    IReadOnlyList<ComponentDiff> ComponentDiffs);

public class CreateVersionValidator : AbstractValidator<CreateVersionCommand>
{
    public CreateVersionValidator()
    {
        RuleFor(x => x.Components).NotEmpty();
        RuleForEach(x => x.Components).ChildRules(c =>
        {
            c.RuleFor(x => x.ChemicalName).NotEmpty();
            c.RuleFor(x => x.MolarMass).GreaterThan(0);
            c.RuleFor(x => x.Proportion).InclusiveBetween(0, 1);
            // Жёсткая привязка к каталогу: вещество должно быть выбрано из PubChem.
            c.RuleFor(x => x.PubChemCid).NotNull().WithMessage("Component must be selected from the chemical catalog.");
            c.RuleFor(x => x.PubChemCid).GreaterThan(0).When(x => x.PubChemCid.HasValue);
        });
        RuleFor(x => x.Components)
            .Must(c => Math.Abs(c.Sum(x => x.Proportion) - 1.0) <= 0.001)
            .WithMessage("Sum of proportions must equal 1.0 (tolerance 0.001).");
    }
}

public class CreateFormulationHandler : IRequestHandler<CreateFormulationCommand, FormulationDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public CreateFormulationHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<FormulationDto> Handle(CreateFormulationCommand request, CancellationToken ct)
    {
        if (!await _auth.OwnsProjectAsync(request.ProjectId, request.UserId, ct))
            throw new ForbiddenException();

        var formulation = new Formulation
        {
            ProjectId = request.ProjectId,
            Name = request.Name,
            TargetPurpose = request.TargetPurpose
        };
        _db.Formulations.Add(formulation);
        await _db.SaveChangesAsync(ct);
        return new FormulationDto(formulation.Id, formulation.ProjectId, formulation.Name,
            formulation.TargetPurpose, formulation.CurrentVersionNumber);
    }
}

public class CreateVersionHandler : IRequestHandler<CreateVersionCommand, FormulationVersionDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    private readonly IChemicalCatalogService _catalog;

    public CreateVersionHandler(IAppDbContext db, ResourceAuthorization auth, IChemicalCatalogService catalog)
        => (_db, _auth, _catalog) = (db, auth, catalog);

    public async Task<FormulationVersionDto> Handle(CreateVersionCommand request, CancellationToken ct)
    {
        if (!await _auth.OwnsFormulationAsync(request.FormulationId, request.CreatedBy, ct))
            throw new ForbiddenException();

        var formulation = await _db.Formulations.FindAsync([request.FormulationId], ct)
                          ?? throw new NotFoundException($"Formulation {request.FormulationId} not found.");

        // Серверная верификация: все CID должны существовать в каталоге.
        // Свойства (имя, формула, масса, CAS) берём ТОЛЬКО из каталога — значениям клиента не доверяем.
        var cids = request.Components.Select(c => c.PubChemCid!.Value).Distinct().ToList();
        var catalogEntries = await _catalog.GetByCidsAsync(cids, ct);
        var byCid = catalogEntries.ToDictionary(e => e.PubChemCid);
        var unknownCid = cids.FirstOrDefault(cid => !byCid.ContainsKey(cid));
        if (unknownCid != 0)
        {
            throw new BadRequestException(
                $"Component with PubChem CID {unknownCid} is not present in the chemical catalog. Resolve it via /api/chemicals first.");
        }

        var versionNumber = formulation.NextVersionNumber();
        var version = new FormulationVersion
        {
            FormulationId = formulation.Id,
            VersionNumber = versionNumber,
            Notes = request.Notes,
            CreatedBy = request.CreatedBy,
            Conditions = new FormulationConditions
            {
                TemperatureCelsius = request.Conditions.TemperatureCelsius,
                PressureKPa = request.Conditions.PressureKPa,
                PhTarget = request.Conditions.PhTarget,
                Solvent = request.Conditions.Solvent,
                DeliveryTarget = request.Conditions.DeliveryTarget
            }
        };
        foreach (var c in request.Components)
        {
            var entry = byCid[c.PubChemCid!.Value];
            version.Components.Add(new FormulationComponent
            {
                PubChemCid = entry.PubChemCid,
                ChemicalName = entry.Name,
                CasNumber = entry.CasNumber,
                Formula = entry.Formula,
                MolarMass = entry.MolarMass,
                Proportion = c.Proportion,
                Role = c.Role
            });
        }
        version.EnsureValid();

        formulation.CurrentVersionNumber = versionNumber;
        _db.FormulationVersions.Add(version);
        await _db.SaveChangesAsync(ct);

        return FormulationMappings.MapVersion(version);
    }
}

public class ListVersionsHandler : IRequestHandler<ListVersionsQuery, IReadOnlyList<FormulationVersionDto>>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public ListVersionsHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<IReadOnlyList<FormulationVersionDto>> Handle(ListVersionsQuery request, CancellationToken ct)
    {
        if (!await _auth.OwnsFormulationAsync(request.FormulationId, request.UserId, ct))
            throw new ForbiddenException();

        var versions = await _db.FormulationVersions
            .Where(v => v.FormulationId == request.FormulationId)
            .Include(v => v.Components)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync(ct);
        return versions.Select(FormulationMappings.MapVersion).ToList();
    }
}

public class ListFormulationsByProjectHandler : IRequestHandler<ListFormulationsByProjectQuery, IReadOnlyList<FormulationDto>>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public ListFormulationsByProjectHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<IReadOnlyList<FormulationDto>> Handle(ListFormulationsByProjectQuery request, CancellationToken ct)
    {
        if (!await _auth.OwnsProjectAsync(request.ProjectId, request.UserId, ct))
            throw new ForbiddenException();

        return await _db.Formulations
            .Where(f => f.ProjectId == request.ProjectId)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => new FormulationDto(f.Id, f.ProjectId, f.Name, f.TargetPurpose, f.CurrentVersionNumber))
            .ToListAsync(ct);
    }
}

public class GetVersionHandler : IRequestHandler<GetVersionQuery, FormulationVersionDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public GetVersionHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<FormulationVersionDto> Handle(GetVersionQuery request, CancellationToken ct)
    {
        var version = await _db.FormulationVersions
            .Include(v => v.Components)
            .FirstOrDefaultAsync(v => v.Id == request.VersionId, ct)
            ?? throw new NotFoundException($"Version {request.VersionId} not found.");
        if (!await _auth.OwnsVersionAsync(request.VersionId, request.UserId, ct))
            throw new ForbiddenException();

        return FormulationMappings.MapVersion(version);
    }
}

public class CompareVersionsHandler : IRequestHandler<CompareVersionsQuery, VersionComparisonDto>
{
    private readonly IAppDbContext _db;
    private readonly ResourceAuthorization _auth;
    public CompareVersionsHandler(IAppDbContext db, ResourceAuthorization auth) => (_db, _auth) = (db, auth);

    public async Task<VersionComparisonDto> Handle(CompareVersionsQuery request, CancellationToken ct)
    {
        if (!await _auth.OwnsFormulationAsync(request.FormulationId, request.UserId, ct))
            throw new ForbiddenException();

        var a = await Load(request.VersionAId, ct);
        var b = await Load(request.VersionBId, ct);

        var names = a.Components.Select(c => c.ChemicalName)
            .Concat(b.Components.Select(c => c.ChemicalName)).Distinct().ToList();
        var diffs = new List<ComponentDiff>();
        foreach (var name in names)
        {
            var ca = a.Components.FirstOrDefault(c => c.ChemicalName == name);
            var cb = b.Components.FirstOrDefault(c => c.ChemicalName == name);
            string change;
            if (ca == null) change = "Added";
            else if (cb == null) change = "Removed";
            else change = "Modified";
            diffs.Add(new ComponentDiff(name, change,
                ca?.Proportion, cb?.Proportion, ca?.MolarMass, cb?.MolarMass));
        }
        return new VersionComparisonDto(FormulationMappings.MapVersion(a), FormulationMappings.MapVersion(b), diffs);
    }

    private Task<FormulationVersion> Load(Guid id, CancellationToken ct) =>
        _db.FormulationVersions.Include(v => v.Components)
            .FirstOrDefaultAsync(v => v.Id == id, ct)
            .ContinueWith(t => t.Result ?? throw new NotFoundException($"Version {id} not found."), ct);
}

internal static class FormulationMappings
{
    public static FormulationVersionDto MapVersion(FormulationVersion v)
    {
        var components = v.Components.Select(c => new ComponentDto(c.Id, c.ChemicalName, c.CasNumber,
            c.Formula, c.MolarMass, c.Proportion, c.Role, c.PubChemCid)).ToList();
        var conditions = new ConditionsDto(v.Conditions.TemperatureCelsius, v.Conditions.PressureKPa,
            v.Conditions.PhTarget, v.Conditions.Solvent, v.Conditions.DeliveryTarget);
        return new FormulationVersionDto(v.Id, v.FormulationId, v.VersionNumber, v.Status.ToString(),
            v.Notes, v.CreatedAtUtc, components, conditions);
    }
}
