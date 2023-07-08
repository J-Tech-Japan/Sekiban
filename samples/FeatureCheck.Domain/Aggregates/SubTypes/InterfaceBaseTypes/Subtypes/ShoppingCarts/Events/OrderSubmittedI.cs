using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts;
using Microsoft.Extensions.Logging;
using Sekiban.Core.Events;
using Sekiban.Core.PubSub;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Events;

public record OrderSubmittedI : IEventPayload<ShoppingCartI, PurchasedCartI, OrderSubmittedI>
{
    public DateTime OrderSubmittedLocalTime { get; init; }

    public static PurchasedCartI OnEvent(ShoppingCartI aggregatePayload, Event<OrderSubmittedI> ev) =>
        new() { Items = aggregatePayload.Items, PurchasedDate = ev.Payload.OrderSubmittedLocalTime };
    public PurchasedCartI OnEventInstance(ShoppingCartI aggregatePayload, Event<OrderSubmittedI> ev) => OnEvent(aggregatePayload, ev);


    public class Subscriber : IEventSubscriber<OrderSubmittedI>
    {
        private readonly ILogger<Subscriber> _logger;
        public Subscriber(ILogger<Subscriber> logger) => _logger = logger;

        public async Task HandleEventAsync(Event<OrderSubmittedI> ev)
        {
            _logger.LogInformation("{PayloadOrderSubmittedLocalTime}", ev.Payload.OrderSubmittedLocalTime);
            await Task.CompletedTask;
        }
    }
}
