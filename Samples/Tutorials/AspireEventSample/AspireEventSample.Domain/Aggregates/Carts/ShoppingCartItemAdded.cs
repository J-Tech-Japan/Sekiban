using Orleans;
using Sekiban.Pure.Events;
namespace AspireEventSample.Domain.Aggregates.Carts;

[GenerateSerializer]
public record ShoppingCartItemAdded(string Name, int Quantity, Guid ItemId, int Price) : IEventPayload;