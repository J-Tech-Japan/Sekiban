using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShippingCarts;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts.Events;

public class PaymentCompletedI : IEventPayload<PurchasedCartI, ShippingCartI, PaymentCompletedI>
{
    public ShippingCartI OnEventInstance(PurchasedCartI aggregatePayload, Event<PaymentCompletedI> ev)
    {
        return OnEvent(aggregatePayload, ev);
    }
    public static ShippingCartI OnEvent(PurchasedCartI aggregatePayload, Event<PaymentCompletedI> ev)
    {
        return new()
            { Items = aggregatePayload.Items };
    }
}
