using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace AspireEventSample.ApiService.Aggregates.Carts;
[GenerateSerializer]
public record AddItemToShoppingCart(Guid ShoppingCartId, string Name, int Quantity, Guid ItemId, int Price) : ICommandWithHandlerAsync<AddItemToShoppingCart, ShoppingCartProjector>
{
    public PartitionKeys SpecifyPartitionKeys(AddItemToShoppingCart command) =>
        PartitionKeys<ShoppingCartProjector>.Existing(command.ShoppingCartId);
    public Task<ResultBox<EventOrNone>> HandleAsync(
        AddItemToShoppingCart command,
        ICommandContext<IAggregatePayload> context) =>
        EventOrNone.Event(new ShoppingCartItemAdded(command.Name, command.Quantity, command.ItemId, command.Price)).ToTask();
}