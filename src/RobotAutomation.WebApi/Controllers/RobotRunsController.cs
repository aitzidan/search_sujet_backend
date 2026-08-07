using MediatR;
using Microsoft.AspNetCore.Mvc;
using RobotAutomation.Application.Runs;
using RobotAutomation.Domain.Enums;
using RobotAutomation.WebApi.Contracts;

namespace RobotAutomation.WebApi.Controllers;

[ApiController]
[Route("api/robot-runs")]
public sealed class RobotRunsController : ControllerBase
{
    private readonly ISender _sender;

    public RobotRunsController(ISender sender) => _sender = sender;

    [HttpPost]
    [ProducesResponseType(typeof(StartRobotRunResult), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<StartRobotRunResult>> Start([FromBody] StartRunRequest request)
    {
        var result = await _sender.Send(
            new StartRobotRunCommand(request.RobotKey, request.PortalName, request.Parameters));
        return Accepted(result.StatusUrl, result);
    }

    [HttpGet("{runId:guid}")]
    [ProducesResponseType(typeof(RobotRunView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<RobotRunView> Get(Guid runId) => _sender.Send(new GetRunStatusQuery(runId));

    [HttpGet]
    public Task<IReadOnlyList<RunSummaryView>> List([FromQuery] RobotStatus? status) =>
        _sender.Send(new ListRunsQuery(status));

    [HttpPost("{runId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid runId)
    {
        await _sender.Send(new CancelRunCommand(runId));
        return Accepted();
    }
}
