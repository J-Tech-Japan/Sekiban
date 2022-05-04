namespace CustomerDomainContext.Aggregates.RecentInMemoryActivities.Events;

public record RecentInMemoryActivityCreated
    (Guid AggregateId, RecentInMemoryActivityRecord Activity) :
        CreateAggregateEvent<RecentInMemoryActivity>(AggregateId);
