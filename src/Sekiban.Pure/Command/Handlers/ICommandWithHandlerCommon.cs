using Sekiban.Pure.Aggregates;
namespace Sekiban.Pure.Command.Handlers;

public interface ICommandWithHandlerCommon<TCommand, TAggregatePayload> : ICommand,
    ICommandHandlerCommon<TCommand, TAggregatePayload>,
    ICommandGetProjector,
    ICommandPartitionSpecifier<TCommand> where TCommand : ICommand, IEquatable<TCommand>
    where TAggregatePayload : IAggregatePayload;
