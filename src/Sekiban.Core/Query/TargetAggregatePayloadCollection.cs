using Sekiban.Core.Aggregate;
using Sekiban.Core.Types;
using System.Collections.Immutable;
namespace Sekiban.Core.Query;

/// <summary>
///     Target Aggregate Payload Collection.
///     Projections can specify which aggregate events to use for the projection.
/// </summary>
public class TargetAggregatePayloadCollection
{
    private ImmutableList<Type> TargetAggregatePayloads { get; set; } = ImmutableList<Type>.Empty;
    public TargetAggregatePayloadCollection Add<TAggregatePayload>() where TAggregatePayload : IAggregatePayloadCommon
    {
        TargetAggregatePayloads = TargetAggregatePayloads.Add(typeof(TAggregatePayload));
        return this;
    }
    public TargetAggregatePayloadCollection Add<TAggregatePayload, TAggregatePayload2>()
        where TAggregatePayload : IAggregatePayloadCommon where TAggregatePayload2 : IAggregatePayloadCommon
    {
        Add<TAggregatePayload>();
        Add<TAggregatePayload2>();
        return this;
    }
    public TargetAggregatePayloadCollection Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3>()
        where TAggregatePayload : IAggregatePayloadCommon
        where TAggregatePayload2 : IAggregatePayloadCommon
        where TAggregatePayload3 : IAggregatePayloadCommon
    {
        Add<TAggregatePayload, TAggregatePayload2>();
        Add<TAggregatePayload3>();
        return this;
    }
    public TargetAggregatePayloadCollection
        Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3, TAggregatePayload4>()
        where TAggregatePayload : IAggregatePayloadCommon
        where TAggregatePayload2 : IAggregatePayloadCommon
        where TAggregatePayload3 : IAggregatePayloadCommon
        where TAggregatePayload4 : IAggregatePayloadCommon
    {
        Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3>();
        Add<TAggregatePayload4>();
        return this;
    }
    public TargetAggregatePayloadCollection
        Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3, TAggregatePayload4, TAggregatePayload5>()
        where TAggregatePayload : IAggregatePayloadCommon
        where TAggregatePayload2 : IAggregatePayloadCommon
        where TAggregatePayload3 : IAggregatePayloadCommon
        where TAggregatePayload4 : IAggregatePayloadCommon
        where TAggregatePayload5 : IAggregatePayloadCommon
    {
        Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3, TAggregatePayload5>();
        Add<TAggregatePayload4>();
        return this;
    }
    public TargetAggregatePayloadCollection
        Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3, TAggregatePayload4, TAggregatePayload5,
            TAggregatePayload6>() where TAggregatePayload : IAggregatePayloadCommon
        where TAggregatePayload2 : IAggregatePayloadCommon
        where TAggregatePayload3 : IAggregatePayloadCommon
        where TAggregatePayload4 : IAggregatePayloadCommon
        where TAggregatePayload5 : IAggregatePayloadCommon
        where TAggregatePayload6 : IAggregatePayloadCommon
    {
        Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3, TAggregatePayload4, TAggregatePayload5>();
        Add<TAggregatePayload6>();
        return this;
    }
    public TargetAggregatePayloadCollection
        Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3, TAggregatePayload4, TAggregatePayload5,
            TAggregatePayload6, TAggregatePayload7>() where TAggregatePayload : IAggregatePayloadCommon
        where TAggregatePayload2 : IAggregatePayloadCommon
        where TAggregatePayload3 : IAggregatePayloadCommon
        where TAggregatePayload4 : IAggregatePayloadCommon
        where TAggregatePayload5 : IAggregatePayloadCommon
        where TAggregatePayload6 : IAggregatePayloadCommon
        where TAggregatePayload7 : IAggregatePayloadCommon
    {
        Add<TAggregatePayload, TAggregatePayload2, TAggregatePayload3, TAggregatePayload4, TAggregatePayload5,
            TAggregatePayload6>();
        Add<TAggregatePayload7>();
        return this;
    }
    public List<string> GetAggregateNames()
    {
        return TargetAggregatePayloads.Select(e => e.GetBaseAggregatePayloadTypeFromAggregate().Name).ToList();
    }
}
