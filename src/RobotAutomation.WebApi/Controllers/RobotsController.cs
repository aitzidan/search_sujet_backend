using MediatR;
using Microsoft.AspNetCore.Mvc;
using RobotAutomation.Application.Robots;

namespace RobotAutomation.WebApi.Controllers;

/// <summary>Lists the automation robots the server can run.</summary>
[ApiController]
[Route("api/robots")]
public sealed class RobotsController : ControllerBase
{
    private readonly ISender _sender;

    public RobotsController(ISender sender) => _sender = sender;

    /// <summary>All registered robots (key, display name, step names).</summary>
    [HttpGet]
    public Task<IReadOnlyList<RobotInfo>> List() => _sender.Send(new ListRobotsQuery());
}
