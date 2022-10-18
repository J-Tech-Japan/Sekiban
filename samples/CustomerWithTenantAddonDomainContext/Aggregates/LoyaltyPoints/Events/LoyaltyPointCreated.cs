using Sekiban.Core.Event;
namespace CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.Events;

public record LoyaltyPointCreated(int InitialPoint) : ICreatedEventPayload;
