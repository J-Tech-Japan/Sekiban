using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace Sekiban.Core.Query.MultiProjections;

public class TargetAggregatePayloadCollection
{
    private ImmutableList<Type> TargetAggregatePayloads { get; set; } = ImmutableList<Type>.Empty;
    public TargetAggregatePayloadCollection Add<TAggregatePayload>() where TAggregatePayload : IAggregatePayload
    {
        TargetAggregatePayloads = TargetAggregatePayloads.Add(typeof(TAggregatePayload));
        return this;
    }
    public TargetAggregatePayloadCollection Add<TAggregatePayload, TAggregatePayload2>() where TAggregatePayload : IAggregatePayload
        where TAggregatePayload2 : IAggregatePayload
    {
        Add<TAggregatePayload>();
        Add<TAggregatePayload2>();
        return this;
    }
    public TargetAggregatePayloadCollection Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3>()
        where TAggregatePayload : IAggregatePayload
        where TAggregatePayload2 : IAggregatePayload
        where TAggregatePayload3 : IAggregatePayload
    {
        Add<TAggregatePayload, TAggregatePayload2>();
        Add<TAggregatePayload3>();
        return this;
    }
    public TargetAggregatePayloadCollection Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3, TAggregatePayload4>()
        where TAggregatePayload : IAggregatePayload
        where TAggregatePayload2 : IAggregatePayload
        where TAggregatePayload3 : IAggregatePayload
        where TAggregatePayload4 : IAggregatePayload
    {
        Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3>();
        Add<TAggregatePayload4>();
        return this;
    }
    public TargetAggregatePayloadCollection Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3, TAggregatePayload4, TAggregatePayload5>()
        where TAggregatePayload : IAggregatePayload
        where TAggregatePayload2 : IAggregatePayload
        where TAggregatePayload3 : IAggregatePayload
        where TAggregatePayload4 : IAggregatePayload
        where TAggregatePayload5 : IAggregatePayload
    {
        Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3, TAggregatePayload5>();
        Add<TAggregatePayload4>();
        return this;
    }
    public TargetAggregatePayloadCollection Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3, TAggregatePayload4, TAggregatePayload5,
        TAggregatePayload6>()
        where TAggregatePayload : IAggregatePayload
        where TAggregatePayload2 : IAggregatePayload
        where TAggregatePayload3 : IAggregatePayload
        where TAggregatePayload4 : IAggregatePayload
        where TAggregatePayload5 : IAggregatePayload
        where TAggregatePayload6 : IAggregatePayload
    {
        Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3, TAggregatePayload4, TAggregatePayload5>();
        Add<TAggregatePayload6>();
        return this;
    }
    public TargetAggregatePayloadCollection Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3, TAggregatePayload4, TAggregatePayload5,
        TAggregatePayload6, TAggregatePayload7>()
        where TAggregatePayload : IAggregatePayload
        where TAggregatePayload2 : IAggregatePayload
        where TAggregatePayload3 : IAggregatePayload
        where TAggregatePayload4 : IAggregatePayload
        where TAggregatePayload5 : IAggregatePayload
        where TAggregatePayload6 : IAggregatePayload
        where TAggregatePayload7 : IAggregatePayload
    {
        Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3, TAggregatePayload4, TAggregatePayload5, TAggregatePayload6>();
        Add<TAggregatePayload7>();
        return this;
    }
    public IList<string> GetAggregateNames() => TargetAggregatePayloads.Select(e => e.Name).ToList();
}
