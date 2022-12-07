using Sekiban.Core.Event;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Events;

public record RecentActivityAdded(RecentActivityRecord Record) : IEventPayload<RecentActivity>
{
    public RecentActivity OnEvent(RecentActivity payload, IEvent ev)
    {
        return new RecentActivity(
            payload.LatestActivities.Add(Record)
                .OrderByDescending(m => m.OccuredAt)
                .Take(5)
                .ToImmutableList());
    }
}
