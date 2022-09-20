namespace CustomerDomainContext.Aggregates.Clients.Events;

public record ClientDeleted : IChangedAggregateEventPayload<Client>;
