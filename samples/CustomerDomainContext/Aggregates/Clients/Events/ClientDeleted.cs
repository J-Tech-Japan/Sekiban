namespace CustomerDomainContext.Aggregates.Clients.Events;

public record ClientDeleted(
    Guid ClientId
) : ChangeAggregateEvent<Client>(
    ClientId
);
