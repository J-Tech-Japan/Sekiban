using Sekiban.Core.Events;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Events;

public record RecentInMemoryActivityAdded(RecentInMemoryActivityRecord Record)
    : IEventPayload<RecentInMemoryActivity, RecentInMemoryActivityAdded>
{
    public static RecentInMemoryActivity OnEvent(RecentInMemoryActivity aggregatePayload,
        Event<RecentInMemoryActivityAdded> ev)
    {
        return new RecentInMemoryActivity(
            aggregatePayload.LatestActivities.Add(ev.Payload.Record).OrderByDescending(m => m.OccuredAt).Take(5)
                .ToImmutableList());
    }
}
