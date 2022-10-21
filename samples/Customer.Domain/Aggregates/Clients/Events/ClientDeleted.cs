using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Events;

public record ClientDeleted : IChangedAggregateEventPayload<Client>;
