namespace CustomerDomainContext.Aggregates.RecentInMemoryActivities.Events;

public record RecentInMemoryActivityAdded(RecentInMemoryActivityRecord Record) : IChangedEventPayload;