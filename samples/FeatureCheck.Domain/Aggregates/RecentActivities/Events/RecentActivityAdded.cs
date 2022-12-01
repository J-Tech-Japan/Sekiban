using System.Collections.Immutable;
using Sekiban.Core.Event;

namespace Customer.Domain.Aggregates.RecentActivities.Events;

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
