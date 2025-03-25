using Orleans;
using Sekiban.Pure.Events;
namespace AspireEventSample.Domain.Aggregates.Carts;

[GenerateSerializer]
public record ShoppingCartCreated(Guid UserId) : IEventPayload;