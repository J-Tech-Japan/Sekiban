using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
namespace AspireEventSample.ApiService.Aggregates.Carts;
[GenerateSerializer]
public record CreateShoppingCart(Guid UserId) : ICommandWithHandlerAsync<CreateShoppingCart, ShoppingCartProjector>
{
    public PartitionKeys SpecifyPartitionKeys(CreateShoppingCart command) =>
        PartitionKeys<ShoppingCartProjector>.Generate();
    public Task<ResultBox<EventOrNone>> HandleAsync(
        CreateShoppingCart command,
        ICommandContext<IAggregatePayload> context) =>
        EventOrNone.Event(new ShoppingCartCreated(command.UserId)).ToTask();
}