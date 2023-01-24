using Sekiban.Core.Events;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Events;

public record RecentInMemoryActivityCreated
    (RecentInMemoryActivityRecord Activity) : IEventPayload<RecentInMemoryActivity, RecentInMemoryActivityCreated>
{
    public RecentInMemoryActivity OnEventInstance(RecentInMemoryActivity payload, Event<RecentInMemoryActivityCreated> ev) =>
        OnEvent(payload, ev);
    public static RecentInMemoryActivity OnEvent(RecentInMemoryActivity payload, Event<RecentInMemoryActivityCreated> ev) =>
        new(ImmutableList<RecentInMemoryActivityRecord>.Empty.Add(ev.Payload.Activity));
}
