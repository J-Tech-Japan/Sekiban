using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Projectors;
using System.Diagnostics.CodeAnalysis;
using ResultBoxes;

namespace Sekiban.Pure.Command.Handlers;

public interface
    ICommandWithHandlerAsync<TCommand, TProjector> :
    ICommandWithHandlerAsync<TCommand, TProjector, IAggregatePayload>,
    ICommandHandlerAsync<TCommand, IAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
}
public interface
    ICommandWithHandlerAsync<TCommand, TProjector, TAggregatePayload> :
    ICommandWithHandlerCommon<TCommand, NoInjection, TAggregatePayload>,
    ICommandHandlerAsync<TCommand, TAggregatePayload>,ICommandWithHandlerSerializable where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
    where TAggregatePayload : IAggregatePayload
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandlerAsync<,,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
    Delegate ICommandWithHandlerSerializable.GetHandler() => HandleAsync;
    Delegate ICommandWithHandlerSerializable.GetPartitionKeysSpecifier() => SpecifyPartitionKeys;
    OptionalValue<Type> ICommandWithHandlerSerializable.GetAggregatePayloadType() => typeof(TAggregatePayload).Name == nameof(IAggregatePayload) ? OptionalValue<Type>.Empty : new OptionalValue<Type>(typeof(TAggregatePayload));

}
