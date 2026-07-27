using MediatR;
using RobotAutomation.Domain.Common;

namespace RobotAutomation.Application.Runs;

public sealed record GetRunStatusQuery(Guid RunId) : IRequest<RobotRunView>;

internal sealed class GetRunStatusHandler : IRequestHandler<GetRunStatusQuery, RobotRunView>
{
    private readonly IRunStore _store;

    public GetRunStatusHandler(IRunStore store) => _store = store;

    public Task<RobotRunView> Handle(GetRunStatusQuery query, CancellationToken ct)
    {
        var run = _store.Get(query.RunId) ?? throw NotFoundException.Run(query.RunId);
        return Task.FromResult(run.ToView());
    }
}
