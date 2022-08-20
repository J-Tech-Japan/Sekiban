namespace CustomerDomainContext.Aggregates.RecentInMemoryActivities.Events
{
    public record RecentInMemoryActivityCreated(RecentInMemoryActivityRecord Activity) : ICreatedEventPayload;
}
