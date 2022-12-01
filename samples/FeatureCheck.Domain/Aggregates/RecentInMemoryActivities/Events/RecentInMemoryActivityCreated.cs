using System.Collections.Immutable;
using Sekiban.Core.Event;

namespace Customer.Domain.Aggregates.RecentInMemoryActivities.Events;

public record RecentInMemoryActivityCreated
    (RecentInMemoryActivityRecord Activity) : IEventPayload<RecentInMemoryActivity>
{
    public RecentInMemoryActivity OnEvent(RecentInMemoryActivity payload, IEvent ev)
    {
        return new(ImmutableList<RecentInMemoryActivityRecord>.Empty.Add(Activity));
    }
}
