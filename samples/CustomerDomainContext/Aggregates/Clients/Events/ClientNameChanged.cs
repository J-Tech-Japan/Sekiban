using Sekiban.Core.Event;
namespace CustomerDomainContext.Aggregates.Clients.Events;

public record ClientNameChanged(string ClientName) : IChangedAggregateEventPayload<Client>;
