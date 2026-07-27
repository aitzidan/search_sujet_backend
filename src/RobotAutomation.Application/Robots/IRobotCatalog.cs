using RobotAutomation.Application.Robots.Abstractions;
using RobotAutomation.Domain.Common;

namespace RobotAutomation.Application.Robots;

/// <summary>Lookup over all registered robots. Backed by the DI-registered <c>IEnumerable&lt;IRobot&gt;</c>.</summary>
public interface IRobotCatalog
{
    IReadOnlyCollection<IRobot> All { get; }
    IRobot? Find(string key);
    IRobot GetRequired(string key);
}

public sealed class RobotCatalog : IRobotCatalog
{
    private readonly IReadOnlyDictionary<string, IRobot> _byKey;

    public RobotCatalog(IEnumerable<IRobot> robots) =>
        _byKey = robots.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IRobot> All => _byKey.Values.ToList();

    public IRobot? Find(string key) => _byKey.GetValueOrDefault(key);

    public IRobot GetRequired(string key) =>
        Find(key) ?? throw NotFoundException.Robot(key);
}
