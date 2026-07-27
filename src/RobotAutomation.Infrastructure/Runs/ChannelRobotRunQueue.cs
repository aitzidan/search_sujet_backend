using System.Threading.Channels;
using RobotAutomation.Application.Runs;

namespace RobotAutomation.Infrastructure.Runs;

/// <summary>
/// Bounded in-memory queue decoupling "start a run" (the API, returns immediately) from
/// "execute a run" (the background worker). A durable queue could replace it behind the seam.
/// </summary>
internal sealed class ChannelRobotRunQueue : IRobotRunQueue
{
    private readonly Channel<RobotRunRequest> _channel =
        Channel.CreateBounded<RobotRunRequest>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

    public ValueTask EnqueueAsync(RobotRunRequest request, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(request, ct);

    public ValueTask<RobotRunRequest> DequeueAsync(CancellationToken ct) =>
        _channel.Reader.ReadAsync(ct);
}
