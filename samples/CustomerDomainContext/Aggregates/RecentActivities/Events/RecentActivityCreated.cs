namespace CustomerDomainContext.Aggregates.RecentActivities.Events;

public record RecentActivityCreated(RecentActivityRecord Activity) : ICreatedEventPayload;
