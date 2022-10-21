using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Events;

public record ClientCreated(Guid BranchId, string ClientName, string ClientEmail) : ICreatedAggregateEventPayload<Client>;
