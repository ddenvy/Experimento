using Experimento.Application.Features.Simulations;
using Experimento.Application.Abstractions;
using Experimento.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Experimento.WebApi.Controllers;

public class SimulationsController : BaseController
{
    public SimulationsController(IMediator mediator, ICurrentUser currentUser, IAuditTrail audit)
        : base(mediator, currentUser, audit) { }

    [HttpGet("formulation-versions/{versionId:guid}/simulations")]
    public async Task<IActionResult> ListRuns(Guid versionId)
        => Ok(await Mediator.Send(new ListSimulationRunsQuery(versionId, UserId)));

    [HttpPost("formulation-versions/{versionId:guid}/simulations")]
    public async Task<IActionResult> Submit(Guid versionId, [FromBody] SubmitSimulationRequest? request)
    {
        var options = request ?? new SubmitSimulationRequest();
        var command = new SubmitSimulationCommand(
            versionId,
            options.Iterations,
            options.VaryConcentrations,
            options.VaryTemperature,
            options.VaryPh,
            options.Seed,
            options.TargetMetric,
            UserId);

        var result = await Mediator.Send(command);
        await AuditAsync("Simulation.Submit", "SimulationJob", result.Id.ToString(), options);
        return Ok(result);
    }

    [HttpGet("simulation-jobs/{jobId:guid}")]
    public async Task<IActionResult> GetJob(Guid jobId)
        => Ok(await Mediator.Send(new GetSimulationJobQuery(jobId, UserId)));

    [HttpGet("simulation-jobs/{jobId:guid}/result")]
    public async Task<IActionResult> GetResult(Guid jobId)
        => Ok(await Mediator.Send(new GetSimulationResultQuery(jobId, UserId)));
}
