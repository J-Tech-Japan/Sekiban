using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using System.Diagnostics.CodeAnalysis;
namespace Sekiban.Pure.Command.Handlers;

public interface
    ICommandHandlerInjectionAsync<TCommand, TInjection, TAggregatePayload> : ICommandHandlerCommon<TCommand, TInjection,
    TAggregatePayload> where TCommand : ICommand, IEquatable<TCommand> where TAggregatePayload : IAggregatePayload
{
    public Task<ResultBox<EventOrNone>> HandleAsync(
        TCommand command,
        TInjection injection,
        ICommandContext<TAggregatePayload> context);
}
public interface
    ICommandWithHandlerInjectionAsync<TCommand, TProjector, TInject, TAggregatePayload> :
    ICommandWithHandlerCommon<TCommand, TInject, TAggregatePayload>,
    ICommandHandlerInjectionAsync<TCommand, TInject, TAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
    where TAggregatePayload : IAggregatePayload
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandlerInjectionAsync<,,,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
