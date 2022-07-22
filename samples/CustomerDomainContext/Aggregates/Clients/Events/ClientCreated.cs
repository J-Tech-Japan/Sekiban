namespace CustomerDomainContext.Aggregates.Clients.Events;

public record ClientCreated(Guid BranchId, string ClientName, string ClientEmail) : IChangedEventPayload;
