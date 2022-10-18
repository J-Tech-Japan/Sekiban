using Sekiban.Core.Event;
namespace CustomerWithTenantAddonDomainContext.Aggregates.RecentActivities.Events;

public record RecentActivityAdded(RecentActivityRecord Record) : IChangedEventPayload;
