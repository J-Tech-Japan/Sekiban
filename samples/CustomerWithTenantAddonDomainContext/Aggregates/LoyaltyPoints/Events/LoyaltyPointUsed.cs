using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.Consts;
using Sekiban.Core.Event;
namespace CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointUsed(DateTime HappenedDate, LoyaltyPointUsageTypeKeys Reason, int PointAmount, string Note) : IChangedEventPayload;
