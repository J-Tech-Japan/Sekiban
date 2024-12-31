using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Projectors;
using System.Diagnostics.CodeAnalysis;
namespace Sekiban.Pure.Command.Handlers;

public interface
    ICommandWithHandlerAsync<TCommand, TProjector> :
    ICommandWithHandlerCommon<TCommand, NoInjection, IAggregatePayload>,
    ICommandHandlerAsync<TCommand, IAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandlerAsync<,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
public interface
    ICommandWithHandlerAsync<TCommand, TProjector, TAggregatePayload> :
    ICommandWithHandlerCommon<TCommand, NoInjection, TAggregatePayload>,
    ICommandHandlerAsync<TCommand, TAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
    where TAggregatePayload : IAggregatePayload
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandlerAsync<,,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
