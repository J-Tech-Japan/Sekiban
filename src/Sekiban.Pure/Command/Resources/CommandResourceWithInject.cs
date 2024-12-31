using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Command.Handlers;

public record CommandResourceWithInject<TCommand, TProjector, TAggregatePayload, TInject>(
    Func<TCommand, PartitionKeys> SpecifyPartitionKeys,
    TInject? Injection,
    Func<TCommand, TInject, ICommandContext<TAggregatePayload>, ResultBox<EventOrNone>> Handler)
    : ICommandResource<TCommand> where TCommand : ICommand, IEquatable<TCommand>
    where TAggregatePayload : IAggregatePayload
    where TProjector : IAggregateProjector, new()
{
    public Func<TCommand, PartitionKeys> GetSpecifyPartitionKeysFunc() => SpecifyPartitionKeys;
    public Type GetCommandType() => typeof(TCommand);
    public IAggregateProjector GetProjector() => new TProjector();
    public object? GetInjection() => Injection;
    public Delegate GetHandler() => Handler;
    public OptionalValue<Type> GetAggregatePayloadType() => typeof(TAggregatePayload);
}
public record CommandResourceWithInject<TCommand, TProjector, TInject>(
    Func<TCommand, PartitionKeys> SpecifyPartitionKeys,
    TInject? Injection,
    Func<TCommand, TInject, ICommandContext<IAggregatePayload>, ResultBox<EventOrNone>> Handler)
    : ICommandResource<TCommand> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
    public Func<TCommand, PartitionKeys> GetSpecifyPartitionKeysFunc() => SpecifyPartitionKeys;
    public Type GetCommandType() => typeof(TCommand);
    public IAggregateProjector GetProjector() => new TProjector();
    public object? GetInjection() => Injection;
    public Delegate GetHandler() => Handler;
    public OptionalValue<Type> GetAggregatePayloadType() => OptionalValue<Type>.Empty;
}
