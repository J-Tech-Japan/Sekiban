using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.RecentActivities.Events;

public record RecentActivityAdded(RecentActivityRecord Record) : IChangedAggregateEventPayload<RecentActivity>;
