using Sekiban.Core.Events;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Events;

public record RecentActivityAdded(RecentActivityRecord Record) : IEventPayload<RecentActivity, RecentActivityAdded>
{
    public static RecentActivity OnEvent(RecentActivity aggregatePayload, Event<RecentActivityAdded> ev)
    {
        return new RecentActivity(
            aggregatePayload
                .LatestActivities
                .Add(ev.Payload.Record)
                .OrderByDescending(m => m.OccuredAt)
                .Take(5)
                .ToImmutableList());
    }
}
