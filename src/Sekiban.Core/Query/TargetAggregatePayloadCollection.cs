using Sekiban.Core.Aggregate;
using System.Collections.Immutable;

namespace Sekiban.Core.Query.MultiProjections;

public class TargetAggregatePayloadCollection
{
    private ImmutableList<IAggregatePayload> TargetAggregatePayloads { get; set; } = ImmutableList<IAggregatePayload>.Empty;
    public TargetAggregatePayloadCollection Add(IAggregatePayload aggregate)
    {
        TargetAggregatePayloads.Add(aggregate);
        return this;
    }
}