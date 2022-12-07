using Sekiban.Core.Event;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Events;

public record RecentActivityCreated(RecentActivityRecord Activity) : IEventPayload<RecentActivity>
{
    public RecentActivity OnEvent(RecentActivity payload, IEvent ev)
    {
        return new(ImmutableList<RecentActivityRecord>.Empty.Add(Activity));
    }
}
