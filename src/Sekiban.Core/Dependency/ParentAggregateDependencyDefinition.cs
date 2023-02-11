using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace Sekiban.Core.Dependency;

public class ParentAggregateDependencyDefinition<TAggregatePayload> : AggregateDependencyDefinition<TAggregatePayload>
    where TAggregatePayload : IParentAggregatePayloadCommon<TAggregatePayload>
{

    public override ImmutableList<(Type, Type?)> CommandTypes
    {
        get
        {
            var types = SelfCommandTypes;
            foreach (var subAggregate in SubAggregates)
            {
                types = types.AddRange(subAggregate.CommandTypes);
            }
            return types;
        }
    }

    public ParentAggregateDependencyDefinition<TAggregatePayload> AddSubAggregate<TSubAggregatePayload>(
        Action<AggregateSubtypeDependencyDefinition<TAggregatePayload, TSubAggregatePayload>> subAggregateDefinitionAction)
        where TSubAggregatePayload : IAggregateSubtypePayload<TAggregatePayload>
    {
        var subAggregate = new AggregateSubtypeDependencyDefinition<TAggregatePayload, TSubAggregatePayload>(this);
        SubAggregates = SubAggregates.Add(subAggregate);
        subAggregateDefinitionAction(subAggregate);
        return this;
    }
}
