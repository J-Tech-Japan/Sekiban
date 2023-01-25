using Sekiban.Core.Events;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Events;

public record RecentInMemoryActivityCreated
    (RecentInMemoryActivityRecord Activity) : IEventPayload<RecentInMemoryActivity, RecentInMemoryActivityCreated>
{
    public static RecentInMemoryActivity OnEvent(RecentInMemoryActivity aggregatePayload, Event<RecentInMemoryActivityCreated> ev) =>
        new(ImmutableList<RecentInMemoryActivityRecord>.Empty.Add(ev.Payload.Activity));
    public RecentInMemoryActivity OnEventInstance(RecentInMemoryActivity aggregatePayload, Event<RecentInMemoryActivityCreated> ev) =>
        OnEvent(aggregatePayload, ev);
}
