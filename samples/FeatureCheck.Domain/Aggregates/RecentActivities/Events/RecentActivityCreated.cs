using System.Collections.Immutable;
using Sekiban.Core.Event;

namespace Customer.Domain.Aggregates.RecentActivities.Events;

public record RecentActivityCreated(RecentActivityRecord Activity) : IEventPayload<RecentActivity>
{
    public RecentActivity OnEvent(RecentActivity payload, IEvent ev)
    {
        return new(ImmutableList<RecentActivityRecord>.Empty.Add(Activity));
    }
}
