using Sekiban.Core.Event;
using System.Collections.Immutable;
namespace Customer.Domain.Aggregates.RecentActivities.Events;

public record RecentActivityCreated(RecentActivityRecord Activity) : ICreatedEvent<RecentActivity>
{
    public RecentActivity OnEvent(RecentActivity payload, IAggregateEvent aggregateEvent)
    {
        return new RecentActivity(ImmutableList<RecentActivityRecord>.Empty.Add(Activity));
    }
}