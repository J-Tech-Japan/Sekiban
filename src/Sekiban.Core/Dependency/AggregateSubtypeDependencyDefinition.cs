using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using System.Collections.Immutable;
namespace Sekiban.Core.Dependency;

public class AggregateSubtypeDependencyDefinition<TParentAggregatePayload, TAggregateSubtypePayload> : IAggregateSubTypeDependencyDefinition<
    TParentAggregatePayload>
    where TParentAggregatePayload : IAggregatePayloadCommon
    where TAggregateSubtypePayload : IAggregatePayloadCommon
{
    internal AggregateSubtypeDependencyDefinition(AggregateDependencyDefinition<TParentAggregatePayload> parentAggregateDependencyDefinition) =>
        ParentAggregateDependencyDefinition = parentAggregateDependencyDefinition;
    public AggregateDependencyDefinition<TParentAggregatePayload> ParentAggregateDependencyDefinition
    {
        get;
        init;
    }
    public ImmutableList<(Type, Type?)> CommandTypes { get; private set; } = ImmutableList<(Type, Type?)>.Empty;
    public AggregateDependencyDefinition<TParentAggregatePayload> GetParentAggregateDependencyDefinition() => ParentAggregateDependencyDefinition;

    public AggregateSubtypeDependencyDefinition<TParentAggregatePayload, TAggregateSubtypePayload>
        AddCommandHandler<TCreateCommand, TCommandHandler>()
        where TCreateCommand : ICommand<TAggregateSubtypePayload>, new()
        where TCommandHandler : ICommandHandlerCommon<TAggregateSubtypePayload, TCreateCommand>
    {
        CommandTypes = CommandTypes.Add(
            (typeof(ICommandHandlerCommon<TAggregateSubtypePayload, TCreateCommand>),
                typeof(TCommandHandler)));
        return this;
    }
}
