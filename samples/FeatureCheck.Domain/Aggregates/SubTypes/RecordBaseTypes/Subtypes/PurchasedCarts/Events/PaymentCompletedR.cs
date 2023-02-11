using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShippingCarts;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts.Events;

public record PaymentCompletedR : IEventPayload<PurchasedCartR, ShippingCartR, PaymentCompletedR>
{
    public ShippingCartR OnEventInstance(PurchasedCartR aggregatePayload, Event<PaymentCompletedR> ev)
    {
        return OnEvent(aggregatePayload, ev);
    }
    public static ShippingCartR OnEvent(PurchasedCartR aggregatePayload, Event<PaymentCompletedR> ev)
    {
        return new ShippingCartR
            { Items = aggregatePayload.Items };
    }
}
