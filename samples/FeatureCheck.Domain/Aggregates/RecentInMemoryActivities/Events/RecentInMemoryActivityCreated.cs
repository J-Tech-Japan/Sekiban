using Sekiban.Core.Event;
using System.Collections.Immutable;
namespace Customer.Domain.Aggregates.RecentInMemoryActivities.Events;

public record RecentInMemoryActivityCreated(RecentInMemoryActivityRecord Activity) : IEventPayload<RecentInMemoryActivity>
{
    public RecentInMemoryActivity OnEvent(RecentInMemoryActivity payload, IEvent ev) =>
        new RecentInMemoryActivity(ImmutableList<RecentInMemoryActivityRecord>.Empty.Add(Activity));
}
