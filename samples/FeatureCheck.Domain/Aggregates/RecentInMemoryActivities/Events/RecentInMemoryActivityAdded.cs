using Sekiban.Core.Events;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Events;

public record RecentInMemoryActivityAdded(RecentInMemoryActivityRecord Record) : IEventPayload<RecentInMemoryActivity, RecentInMemoryActivityAdded>
{
    public RecentInMemoryActivity OnEventInstance(RecentInMemoryActivity payload, Event<RecentInMemoryActivityAdded> ev) => OnEvent(payload, ev);
    public static RecentInMemoryActivity OnEvent(RecentInMemoryActivity payload, Event<RecentInMemoryActivityAdded> ev)
    {
        return new RecentInMemoryActivity(
            payload.LatestActivities.Add(ev.Payload.Record)
                .OrderByDescending(m => m.OccuredAt)
                .Take(5)
                .ToImmutableList());
    }
}
