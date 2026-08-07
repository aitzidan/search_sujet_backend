using RobotAutomation.Application.Robots;

namespace RobotAutomation.Application.Sessions;

public interface IBrowserSessionFactory
{
    Task<IRobotPageSession> CreateSessionAsync(RobotSessionOptions options, CancellationToken ct);
}

public sealed record RobotSessionOptions(string? StorageStatePath = null);

public interface IRobotPageSession : IAsyncDisposable
{
    IRobotPage Page { get; }

    Task SaveStateAsync(CancellationToken ct);
}
