using Experimento.Application.Features.Predictions;
using Experimento.Application.Abstractions;
using Experimento.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Experimento.WebApi.Controllers;

public class PredictionsController : BaseController
{
    public PredictionsController(IMediator mediator, ICurrentUser currentUser, IAuditTrail audit)
        : base(mediator, currentUser, audit) { }

    [HttpPost("formulation-versions/{versionId:guid}/predictions")]
    public async Task<IActionResult> Submit(Guid versionId)
    {
        var result = await Mediator.Send(new SubmitPredictionCommand(versionId, UserId));
        await AuditAsync("Prediction.Submit", "PredictionJob", result.Id.ToString(), versionId);
        return Ok(result);
    }

    [HttpGet("prediction-jobs/{jobId:guid}")]
    public async Task<IActionResult> GetJob(Guid jobId)
        => Ok(await Mediator.Send(new GetPredictionJobQuery(jobId, UserId)));

    [HttpGet("prediction-jobs/{jobId:guid}/result")]
    public async Task<IActionResult> GetResult(Guid jobId)
        => Ok(await Mediator.Send(new GetPredictionResultQuery(jobId, UserId)));

    [HttpPost("prediction-results/{resultId:guid}/review")]
    public async Task<IActionResult> Review(Guid resultId, [FromBody] SubmitReviewCommand cmd)
    {
        var result = await Mediator.Send(cmd with { ResultId = resultId, ReviewerUserId = UserId });
        await AuditAsync("Prediction.Review", "PredictionResult", resultId.ToString(), cmd);
        return Ok(result);
    }

    [HttpPost("prediction-results/{resultId:guid}/outcome")]
    public async Task<IActionResult> RecordOutcome(Guid resultId, [FromBody] RecordOutcomeCommand cmd)
    {
        var result = await Mediator.Send(cmd with { ResultId = resultId, RecordedBy = UserId });
        await AuditAsync("Prediction.Outcome", "PredictionResult", resultId.ToString(), cmd);
        return Ok(result);
    }

    [HttpGet("calibration")]
    public async Task<IActionResult> Calibration()
        => Ok(await Mediator.Send(new GetCalibrationStatsQuery(UserId)));
}
