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

public class CreateFormulationValidator : AbstractValidator<CreateFormulationCommand>
{
    public CreateFormulationValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TargetPurpose).MaximumLength(2000);
    }
}

public class CreateVersionValidator : AbstractValidator<CreateVersionCommand>
{
    public CreateVersionValidator()
    {
        RuleFor(x => x.FormulationId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(4000);
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
        // Одно вещество не может входить в состав двумя строками: ломает сравнение версий
        // и дублирует вклад в предсказание.
        RuleFor(x => x.Components)
            .Must(c => c.Select(x => x.PubChemCid).Distinct().Count() == c.Count)
            .WithMessage("Components must not contain duplicate chemicals.");
        RuleFor(x => x.Components)
            .Must(c => Math.Abs(c.Sum(x => x.Proportion) - 1.0) <= 0.001)
            .WithMessage("Sum of proportions must equal 1.0 (tolerance 0.001).");

        // Физические границы условий — широкие, но отсекают мусор и опечатки на порядки.
        RuleFor(x => x.Conditions.TemperatureCelsius).InclusiveBetween(-100, 500);
        RuleFor(x => x.Conditions.PressureKPa).InclusiveBetween(0, 1_000_000)
            .When(x => x.Conditions.PressureKPa.HasValue);
        RuleFor(x => x.Conditions.PhTarget).InclusiveBetween(0, 14)
            .When(x => x.Conditions.PhTarget.HasValue);
        RuleFor(x => x.Conditions.Solvent).MaximumLength(300);
        RuleFor(x => x.Conditions.DeliveryTarget).MaximumLength(300);
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
                Smiles = entry.Smiles,
                Proportion = c.Proportion,
                Role = c.Role
            });
        }
        version.EnsureValid();

        formulation.CurrentVersionNumber = versionNumber;
        _db.FormulationVersions.Add(version);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Параллельное создание версий: уникальный индекс (FormulationId, VersionNumber)
            // не пропустил одинаковый номер — клиент должен повторить запрос.
            throw new ConflictException(
                $"Version {versionNumber} was created concurrently for formulation {request.FormulationId}. Please retry.");
        }

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

        // Версии грузятся только внутри уже проверенной формуляции: иначе владелец одной
        // формуляции мог бы подставить чужие VersionId и прочитать чужой состав (IDOR).
        var a = await LoadAsync(request.FormulationId, request.VersionAId, ct);
        var b = await LoadAsync(request.FormulationId, request.VersionBId, ct);

        var names = a.Components.Select(c => c.ChemicalName)
            .Concat(b.Components.Select(c => c.ChemicalName)).Distinct(StringComparer.Ordinal).ToList();
        var diffs = new List<ComponentDiff>();
        foreach (var name in names)
        {
            var ca = a.Components.FirstOrDefault(c => c.ChemicalName == name);
            var cb = b.Components.FirstOrDefault(c => c.ChemicalName == name);
            string change;
            if (ca == null) change = "Added";
            else if (cb == null) change = "Removed";
            else
            {
                // Полностью идентичный компонент — не изменение, в диффе ему делать нечего.
                if (ca.Proportion == cb.Proportion && ca.MolarMass == cb.MolarMass)
                    continue;
                change = "Modified";
            }
            diffs.Add(new ComponentDiff(name, change,
                ca?.Proportion, cb?.Proportion, ca?.MolarMass, cb?.MolarMass));
        }
        return new VersionComparisonDto(FormulationMappings.MapVersion(a), FormulationMappings.MapVersion(b), diffs);
    }

    private async Task<FormulationVersion> LoadAsync(Guid formulationId, Guid id, CancellationToken ct)
    {
        var version = await _db.FormulationVersions
            .Include(v => v.Components)
            .FirstOrDefaultAsync(v => v.Id == id && v.FormulationId == formulationId, ct);
        return version ?? throw new NotFoundException($"Version {id} not found in formulation {formulationId}.");
    }
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
