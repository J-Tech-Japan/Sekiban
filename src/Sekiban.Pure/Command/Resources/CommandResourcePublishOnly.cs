using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Command.Handlers;

public record CommandResourcePublishOnly<TCommand>(
    Func<TCommand, PartitionKeys> SpecifyPartitionKeys,
    Func<TCommand, ICommandContextWithoutState, ResultBox<EventOrNone>> Handler) : ICommandResource<TCommand>
    where TCommand : ICommand, IEquatable<TCommand>
{

    public Func<TCommand, PartitionKeys> GetSpecifyPartitionKeysFunc() => SpecifyPartitionKeys;
    public OptionalValue<Type> GetAggregatePayloadType() => OptionalValue<Type>.Empty;
    public Type GetCommandType() => typeof(TCommand);
    public IAggregateProjector GetProjector() => NoneAggregateProjector.Empty;
    public object? GetInjection() => null;
    public Delegate GetHandler() => Handler;
}
