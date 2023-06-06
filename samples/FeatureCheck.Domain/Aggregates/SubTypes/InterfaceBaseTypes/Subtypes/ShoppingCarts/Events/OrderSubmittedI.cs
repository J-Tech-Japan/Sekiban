using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts;
using Sekiban.Core.Events;
using Sekiban.Core.PubSub;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Events;

public record OrderSubmittedI : IEventPayload<ShoppingCartI, PurchasedCartI, OrderSubmittedI>
{
    public DateTime OrderSubmittedLocalTime { get; init; }
    public PurchasedCartI OnEventInstance(ShoppingCartI aggregatePayload, Event<OrderSubmittedI> ev) => OnEvent(aggregatePayload, ev);

    public static PurchasedCartI OnEvent(ShoppingCartI aggregatePayload, Event<OrderSubmittedI> ev) =>
        new() { Items = aggregatePayload.Items, PurchasedDate = ev.Payload.OrderSubmittedLocalTime };


    public class Subscriber : IEventSubscriber<OrderSubmittedI>
    {
        public async Task HandleEventAsync(Event<OrderSubmittedI> ev)
        {
            Console.WriteLine($"{ev.Payload.OrderSubmittedLocalTime}");
            await Task.CompletedTask;
        }
    }
}
