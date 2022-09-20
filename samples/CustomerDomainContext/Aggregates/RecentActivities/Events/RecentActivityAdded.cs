namespace CustomerDomainContext.Aggregates.RecentActivities.Events;

public record RecentActivityAdded(RecentActivityRecord Record) : IChangedAggregateEventPayload<RecentActivity>;
