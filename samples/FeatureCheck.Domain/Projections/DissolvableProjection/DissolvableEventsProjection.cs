using FeatureCheck.Domain.Aggregates.RecentActivities;
using FeatureCheck.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
using Sekiban.Core.Query.MultiProjections;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Projections.DissolvableProjection;

[AggregateContainerGroup(AggregateContainerGroup.Dissolvable)]
public record DissolvableEventsProjection : IMultiProjectionPayload<DissolvableEventsProjection>
{
    public ImmutableList<RecentActivityRecord> RecentActivities { get; init; } = ImmutableList<RecentActivityRecord>.Empty;

    public Func<DissolvableEventsProjection, DissolvableEventsProjection>? GetApplyEventFunc(IEvent ev, IEventPayloadCommon eventPayload) =>
        eventPayload switch
        {
            RecentActivityAdded recentActivityAdded => projection =>
                projection with { RecentActivities = projection.RecentActivities.Add(recentActivityAdded.Record) },
            _ => null
        };
}
