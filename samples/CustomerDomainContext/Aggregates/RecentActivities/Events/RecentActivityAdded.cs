namespace CustomerDomainContext.Aggregates.RecentActivities.Events;

public record RecentActivityAdded(
    Guid AggregateId,
    RecentActivityRecord Record
) : ChangeAggregateEvent<RecentActivity>(
    AggregateId
);
