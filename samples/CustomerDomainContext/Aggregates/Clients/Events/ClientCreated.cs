namespace CustomerDomainContext.Aggregates.Clients.Events;

public record ClientCreated(
    Guid ClientId,
    Guid BranchId,
    string ClientName,
    string ClientEmail
) : CreateAggregateEvent<Client>(
    ClientId
);
