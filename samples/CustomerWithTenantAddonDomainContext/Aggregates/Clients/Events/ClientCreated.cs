using Sekiban.Core.Event;
namespace CustomerWithTenantAddonDomainContext.Aggregates.Clients.Events;

public record ClientCreated(Guid BranchId, string ClientName, string ClientEmail) : ICreatedEventPayload;
