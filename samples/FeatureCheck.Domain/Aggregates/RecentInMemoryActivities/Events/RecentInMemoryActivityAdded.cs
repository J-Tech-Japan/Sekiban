using System.Collections.Immutable;
using Sekiban.Core.Event;

namespace Customer.Domain.Aggregates.RecentInMemoryActivities.Events;

public record RecentInMemoryActivityAdded(RecentInMemoryActivityRecord Record) : IEventPayload<RecentInMemoryActivity>
{
    public RecentInMemoryActivity OnEvent(RecentInMemoryActivity payload, IEvent ev)
    {
        return new RecentInMemoryActivity(
            payload.LatestActivities.Add(Record)
                .OrderByDescending(m => m.OccuredAt)
                .Take(5)
                .ToImmutableList());
    }
}
