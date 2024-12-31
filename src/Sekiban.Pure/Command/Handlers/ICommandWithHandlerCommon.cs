using Sekiban.Pure.Aggregates;
namespace Sekiban.Pure.Command.Handlers;

public interface ICommandWithHandlerCommon<TCommand, TInjection, TAggregatePayload> : ICommand,
    ICommandHandlerCommon<TCommand, TInjection, TAggregatePayload>,
    ICommandGetProjector,
    ICommandPartitionSpecifier<TCommand> where TCommand : ICommand, IEquatable<TCommand>
    where TAggregatePayload : IAggregatePayload;
