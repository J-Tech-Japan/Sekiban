namespace CustomerDomainContext.Aggregates.Clients.Events;

public record ClientNameChanged(Guid ClientId, string ClientName) : ChangeAggregateEvent<Client>(ClientId);
