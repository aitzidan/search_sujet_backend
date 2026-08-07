using MediatR;
using Microsoft.AspNetCore.Mvc;
using RobotAutomation.Application.Robots;

namespace RobotAutomation.WebApi.Controllers;

[ApiController]
[Route("api/robots")]
public sealed class RobotsController : ControllerBase
{
    private readonly ISender _sender;

    public RobotsController(ISender sender) => _sender = sender;

    [HttpGet]
    public Task<IReadOnlyList<RobotInfo>> List() => _sender.Send(new ListRobotsQuery());
}
