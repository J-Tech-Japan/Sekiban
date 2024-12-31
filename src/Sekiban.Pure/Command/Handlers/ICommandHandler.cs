using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using System.Diagnostics.CodeAnalysis;
namespace Sekiban.Pure.Command.Handlers;

public interface
    ICommandHandler<TCommand, TAggregatePayload> : ICommandHandlerCommon<TCommand, NoInjection, TAggregatePayload>
    where TCommand : ICommand, IEquatable<TCommand> where TAggregatePayload : IAggregatePayload
{
    public ResultBox<EventOrNone> Handle(TCommand command, ICommandContext<TAggregatePayload> context);
}
public interface
    ICommandWithHandler<TCommand, TProjector, TAggregatePayload> :
    ICommandWithHandlerCommon<TCommand, NoInjection, TAggregatePayload>,
    ICommandHandler<TCommand, TAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
    where TAggregatePayload : IAggregatePayload
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandler<,,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
