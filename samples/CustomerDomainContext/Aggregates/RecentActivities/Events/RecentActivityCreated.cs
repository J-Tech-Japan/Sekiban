using Sekiban.Core.Event;
namespace CustomerDomainContext.Aggregates.RecentActivities.Events;

public record RecentActivityCreated(RecentActivityRecord Activity) : ICreatedAggregateEventPayload<RecentActivity>;
