namespace CustomerDomainContext.Aggregates.RecentActivities.Events;

public record RecentActivityCreated
    (Guid AggregateId, RecentActivityRecord Activity) : CreateAggregateEvent<RecentActivity>(
        AggregateId);
