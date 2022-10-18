using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.Consts;
using Sekiban.Core.Event;
namespace CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointAdded(DateTime HappenedDate, LoyaltyPointReceiveTypeKeys Reason, int PointAmount, string Note) : IChangedEventPayload;
