using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Projectors;
using System.Diagnostics.CodeAnalysis;
namespace Sekiban.Pure.Command.Handlers;

public interface
    ICommandWithHandler<TCommand, TProjector> : ICommandWithHandlerCommon<TCommand, NoInjection, IAggregatePayload>,
    ICommandHandler<TCommand, IAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandler<,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
