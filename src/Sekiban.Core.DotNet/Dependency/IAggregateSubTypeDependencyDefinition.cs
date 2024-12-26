using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace Sekiban.Core.Dependency;

public interface IAggregateSubTypeDependencyDefinition<TParentAggregatePayload>
    where TParentAggregatePayload : IAggregatePayloadCommon
{
    public ImmutableList<(Type, Type?)> CommandTypes { get; }
    public ImmutableList<(Type, Type?)> SubscriberTypes { get; }
    public AggregateDependencyDefinition<TParentAggregatePayload> GetParentAggregateDependencyDefinition();
}
