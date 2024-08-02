using Sekiban.Core.Events;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Events;

public record RecentActivityCreated(RecentActivityRecord Activity)
    : IEventPayload<RecentActivity, RecentActivityCreated>
{
    public static RecentActivity OnEvent(RecentActivity aggregatePayload, Event<RecentActivityCreated> ev) =>
        new(ImmutableList<RecentActivityRecord>.Empty.Add(ev.Payload.Activity));
}
