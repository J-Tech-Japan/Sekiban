using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.RecentInMemoryActivities.Events;

public record RecentInMemoryActivityCreated(RecentInMemoryActivityRecord Activity) : ICreatedAggregateEventPayload<RecentInMemoryActivity>;
