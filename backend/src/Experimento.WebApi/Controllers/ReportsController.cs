using Experimento.Application.Features.Reports;
using Experimento.Application.Abstractions;
using Experimento.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Experimento.WebApi.Controllers;

public class ReportsController : BaseController
{
    public ReportsController(IMediator mediator, ICurrentUser currentUser, IAuditTrail audit)
        : base(mediator, currentUser, audit) { }

    [HttpGet("formulation-versions/{versionId:guid}/report")]
    public async Task<IActionResult> GetVersionReport(Guid versionId)
    {
        var markdown = await Mediator.Send(new GenerateVersionReportQuery(versionId, UserId));
        await AuditAsync("Report.Download", "FormulationVersion", versionId.ToString());
        var bytes = System.Text.Encoding.UTF8.GetBytes(markdown);
        return File(bytes, "text/markdown; charset=utf-8", $"formulation-{versionId}.md");
    }
}
