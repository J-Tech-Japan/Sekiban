using Sekiban.Core.Aggregate;
using System.Collections.Immutable;

namespace Sekiban.Core.Query.MultiProjections;

public class TargetAggregatePayloadCollection
{
    private ImmutableList<Type> TargetAggregatePayloads { get; set; } = ImmutableList<Type>.Empty;
    public TargetAggregatePayloadCollection Add<TAggregatePayload>() where TAggregatePayload : IAggregatePayload
    {
        TargetAggregatePayloads.Add(typeof(TAggregatePayload));
        return this;
    }
    public IList<string> GetAggregateNames() => TargetAggregatePayloads.Select(e => e.GetType().Name).ToList();
}