using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.PurchasedCarts.Events;

public record PaymentReceivedToCart : IEventPayload<PurchasedCartI, PaymentReceivedToCart>
{

    public static PurchasedCartI OnEvent(PurchasedCartI aggregatePayload, Event<PaymentReceivedToCart> ev) => throw new NotImplementedException();
    public PurchasedCartI OnEventInstance(PurchasedCartI aggregatePayload, Event<PaymentReceivedToCart> ev) => OnEvent(aggregatePayload, ev);
}
