using MediatR;

namespace RobotAutomation.Application.Robots;

/// <summary>Describes a robot for the UI: its key, name, and step names.</summary>
public sealed record RobotInfo(string Key, string DisplayName, IReadOnlyList<string> Steps);

public sealed record ListRobotsQuery : IRequest<IReadOnlyList<RobotInfo>>;

internal sealed class ListRobotsHandler : IRequestHandler<ListRobotsQuery, IReadOnlyList<RobotInfo>>
{
    private readonly IRobotCatalog _catalog;

    public ListRobotsHandler(IRobotCatalog catalog) => _catalog = catalog;

    public Task<IReadOnlyList<RobotInfo>> Handle(ListRobotsQuery query, CancellationToken ct)
    {
        IReadOnlyList<RobotInfo> robots = _catalog.All
            .Select(r => new RobotInfo(r.Key, r.DisplayName, r.Steps.Select(s => s.Name).ToList()))
            .ToList();
        return Task.FromResult(robots);
    }
}
