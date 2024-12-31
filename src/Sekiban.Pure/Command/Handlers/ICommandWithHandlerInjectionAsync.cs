using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Projectors;
using System.Diagnostics.CodeAnalysis;
namespace Sekiban.Pure.Command.Handlers;

public interface
    ICommandWithHandlerInjectionAsync<TCommand, TProjector, TInject> :
    ICommandWithHandlerCommon<TCommand, TInject, IAggregatePayload>,
    ICommandHandlerInjectionAsync<TCommand, TInject, IAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandlerInjectionAsync<,,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
