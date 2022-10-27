using Sekiban.Core.Event;
using System.Collections.Immutable;
namespace Customer.Domain.Aggregates.RecentInMemoryActivities.Events;

public record RecentInMemoryActivityCreated(RecentInMemoryActivityRecord Activity) : ICreatedEvent<RecentInMemoryActivity>
{
    public RecentInMemoryActivity OnEvent(RecentInMemoryActivity payload, IAggregateEvent aggregateEvent)
    {
        return new RecentInMemoryActivity(ImmutableList<RecentInMemoryActivityRecord>.Empty.Add(Activity));
    }
}
