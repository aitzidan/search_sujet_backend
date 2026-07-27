using MediatR;
using RobotAutomation.Domain.Enums;

namespace RobotAutomation.Application.Runs;

public sealed record ListRunsQuery(RobotStatus? Status) : IRequest<IReadOnlyList<RunSummaryView>>;

internal sealed class ListRunsHandler : IRequestHandler<ListRunsQuery, IReadOnlyList<RunSummaryView>>
{
    private readonly IRunStore _store;

    public ListRunsHandler(IRunStore store) => _store = store;

    public Task<IReadOnlyList<RunSummaryView>> Handle(ListRunsQuery query, CancellationToken ct)
    {
        IReadOnlyList<RunSummaryView> summaries =
            _store.List(query.Status).Select(r => r.ToSummary()).ToList();
        return Task.FromResult(summaries);
    }
}
