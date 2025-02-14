using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.Command.Handlers;

public interface ICommandHandlerAsync<TCommand, TAggregatePayload> : ICommandHandlerCommon<TCommand, TAggregatePayload>
    where TCommand : ICommand, IEquatable<TCommand> where TAggregatePayload : IAggregatePayload
{
    public Task<ResultBox<EventOrNone>> HandleAsync(TCommand command, ICommandContext<TAggregatePayload> context);
}
