using CustomerDomainContext.Aggregates.RecentActivities;
namespace CustomerDomainContext.Aggregates.RecentInMemoryActivities.Events;

public record RecentInMemoryActivityAdded(
    Guid AggregateId,
    RecentInMemoryActivityRecord Record
) : ChangeAggregateEvent<RecentInMemoryActivity>(
    AggregateId
);