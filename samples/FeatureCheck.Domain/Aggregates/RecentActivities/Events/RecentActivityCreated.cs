using Sekiban.Core.Events;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Events;

public record RecentActivityCreated(RecentActivityRecord Activity) : IEventPayload<RecentActivity, RecentActivityCreated>
{
    public static RecentActivity OnEvent(RecentActivity payload, Event<RecentActivityCreated> ev) =>
        new(ImmutableList<RecentActivityRecord>.Empty.Add(ev.Payload.Activity));
    public RecentActivity OnEventInstance(RecentActivity payload, Event<RecentActivityCreated> ev) => OnEvent(payload, ev);
}
