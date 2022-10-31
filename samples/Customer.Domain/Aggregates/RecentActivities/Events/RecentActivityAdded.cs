using Sekiban.Core.Event;
using System.Collections.Immutable;
namespace Customer.Domain.Aggregates.RecentActivities.Events;

public record RecentActivityAdded(RecentActivityRecord Record) : IChangedEvent<RecentActivity>
{
    public RecentActivity OnEvent(RecentActivity payload, IEvent @event)
    {
        return new RecentActivity(
            payload.LatestActivities.Add(Record)
                .OrderByDescending(m => m.OccuredAt)
                .Take(5)
                .ToImmutableList());
    }
}
