using Sekiban.Pure.Aggregates;
namespace Sekiban.Pure.Command.Handlers;

public interface
    ICommandHandlerCommon<TCommand, TInjection, TAggregatePayload> : ICommandWithAggregateRestriction<TAggregatePayload>
    where TCommand : ICommand, IEquatable<TCommand> where TAggregatePayload : IAggregatePayload
{
}
