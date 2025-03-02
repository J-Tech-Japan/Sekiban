using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
namespace AspireEventSample.ApiService.Aggregates.Carts;

[GenerateSerializer]
public record ProcessPaymentOnShoppingCart(Guid ShoppingCartId, string PaymentMethod)
    : ICommandWithHandler<ProcessPaymentOnShoppingCart, ShoppingCartProjector>
{
    public PartitionKeys SpecifyPartitionKeys(ProcessPaymentOnShoppingCart command)
        => PartitionKeys<ShoppingCartProjector>.Existing(command.ShoppingCartId);

    public ResultBox<EventOrNone> Handle(
        ProcessPaymentOnShoppingCart command,
        ICommandContext<IAggregatePayload> context)
        => EventOrNone.Event(new ShoppingCartPaymentProcessed(command.PaymentMethod));
}