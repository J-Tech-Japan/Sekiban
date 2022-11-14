using Sekiban.Core.Event;
using System.Collections.Immutable;
namespace Customer.Domain.Aggregates.RecentInMemoryActivities.Events;

public record RecentInMemoryActivityAdded(RecentInMemoryActivityRecord Record) : IChangedEvent<RecentInMemoryActivity>
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
