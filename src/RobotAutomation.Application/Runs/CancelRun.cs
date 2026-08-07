using MediatR;
using RobotAutomation.Domain.Common;

namespace RobotAutomation.Application.Runs;

public sealed record CancelRunCommand(Guid RunId) : IRequest<bool>;

internal sealed class CancelRunHandler : IRequestHandler<CancelRunCommand, bool>
{
    private readonly IRunStore _store;

    public CancelRunHandler(IRunStore store) => _store = store;

    public Task<bool> Handle(CancelRunCommand command, CancellationToken ct)
    {
        if (_store.Get(command.RunId) is null)
            throw NotFoundException.Run(command.RunId);

        return Task.FromResult(_store.TryCancel(command.RunId));
    }
}
