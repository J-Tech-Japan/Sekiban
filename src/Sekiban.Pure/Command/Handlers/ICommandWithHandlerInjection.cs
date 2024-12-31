using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Projectors;
using System.Diagnostics.CodeAnalysis;
namespace Sekiban.Pure.Command.Handlers;

public interface
    ICommandWithHandlerInjection<TCommand, TProjector, TInject, TAggregatePayload> :
    ICommandWithHandlerCommon<TCommand, TInject, TAggregatePayload>,
    ICommandHandlerInjection<TCommand, TInject, TAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
    where TAggregatePayload : IAggregatePayload
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandlerInjection<,,,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
public interface
    ICommandWithHandlerInjection<TCommand, TProjector, TInject> :
    ICommandWithHandlerCommon<TCommand, TInject, IAggregatePayload>,
    ICommandHandlerInjection<TCommand, TInject, IAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandlerInjection<,,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
