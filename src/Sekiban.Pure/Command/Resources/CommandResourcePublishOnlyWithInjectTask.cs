using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Command.Handlers;

public record CommandResourcePublishOnlyWithInjectTask<TCommand, TInject>(
    Func<TCommand, PartitionKeys> SpecifyPartitionKeys,
    TInject? Injection,
    Func<TCommand, TInject, ICommandContextWithoutState, Task<ResultBox<EventOrNone>>> Handler)
    : ICommandResource<TCommand> where TCommand : ICommand, IEquatable<TCommand>
{
    public Func<TCommand, PartitionKeys> GetSpecifyPartitionKeysFunc() => SpecifyPartitionKeys;
    public OptionalValue<Type> GetAggregatePayloadType() => OptionalValue<Type>.Empty;
    public Type GetCommandType() => typeof(TCommand);
    public IAggregateProjector GetProjector() => NoneAggregateProjector.Empty;
    public object? GetInjection() => Injection;
    public Delegate GetHandler() => Handler;
}
