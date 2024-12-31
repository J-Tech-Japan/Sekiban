using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Events;
namespace Pure.Domain;

public record CreateShoppingCart(Guid UserId) : ICommandWithHandlerAsync<CreateShoppingCart, ShoppingCartProjector>
{
    public PartitionKeys SpecifyPartitionKeys(CreateShoppingCart command) =>
        PartitionKeys<ShoppingCartProjector>.Generate();
    public Task<ResultBox<EventOrNone>> HandleAsync(
        CreateShoppingCart command,
        ICommandContext<IAggregatePayload> context) =>
        EventOrNone.Event(new ShoppingCartCreated(command.UserId)).ToTask();
}
