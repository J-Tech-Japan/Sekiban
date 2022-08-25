namespace CustomerWithTenantAddonDomainContext.Aggregates.Clients.Events;

public record ClientNameChanged(string ClientName) : IChangedEventPayload;
