using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Projectors;
using System.Diagnostics.CodeAnalysis;
namespace Sekiban.Pure.Command.Handlers;

public interface
    ICommandWithHandler<TCommand, TProjector> : ICommandWithHandler<TCommand, TProjector, IAggregatePayload>,
    ICommandHandler<TCommand, IAggregatePayload>,
    ICommandWithHandlerSerializable where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
}
public interface
    ICommandWithHandler<TCommand, TProjector, TAggregatePayload> :
    ICommandWithHandlerCommon<TCommand, TAggregatePayload>,
    ICommandHandler<TCommand, TAggregatePayload>,
    ICommandWithHandlerSerializable where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
    where TAggregatePayload : IAggregatePayload
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandler<,,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();

    Delegate ICommandWithHandlerSerializable.GetHandler() => Handle;
    Delegate ICommandWithHandlerSerializable.GetPartitionKeysSpecifier() => SpecifyPartitionKeys;
    OptionalValue<Type> ICommandWithHandlerSerializable.GetAggregatePayloadType() =>
        typeof(TAggregatePayload).Name == nameof(IAggregatePayload)
            ? OptionalValue<Type>.Empty
            : new OptionalValue<Type>(typeof(TAggregatePayload));
}
