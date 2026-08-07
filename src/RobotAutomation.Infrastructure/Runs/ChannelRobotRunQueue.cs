using System.Threading.Channels;
using RobotAutomation.Application.Runs;

namespace RobotAutomation.Infrastructure.Runs;

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
