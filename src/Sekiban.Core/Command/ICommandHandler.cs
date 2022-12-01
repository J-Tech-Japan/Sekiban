using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;

namespace Sekiban.Core.Command;

public interface ICommandHandlerBase<TAggregatePayload, TCommand> : ICommandHandlerCommon<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayload, new() where TCommand : ICommand<TAggregatePayload>
{
    public IAsyncEnumerable<IEventPayload<TAggregatePayload>> HandleCommandAsync(
        Func<AggregateState<TAggregatePayload>> getAggregateState,
        TCommand command);
}
