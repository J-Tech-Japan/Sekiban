using Sekiban.Pure.Aggregates;
namespace Sekiban.Pure.Command.Handlers;

public interface ICommandWithAggregateRestriction<TAggregatePayload> : ICommand
    where TAggregatePayload : IAggregatePayload;
