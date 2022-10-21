using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Events;

public record ClientNameChanged(string ClientName) : IChangedAggregateEventPayload<Client>;
