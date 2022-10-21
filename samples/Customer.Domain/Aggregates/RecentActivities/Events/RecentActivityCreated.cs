using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.RecentActivities.Events;

public record RecentActivityCreated(RecentActivityRecord Activity) : ICreatedAggregateEventPayload<RecentActivity>;
