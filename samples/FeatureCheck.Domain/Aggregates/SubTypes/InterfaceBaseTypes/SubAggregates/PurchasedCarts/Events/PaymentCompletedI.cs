using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.ShippingCarts;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.PurchasedCarts.Events;

public class PaymentCompletedI : IEventPayload<PurchasedCartI, ShippingCartI, PaymentCompletedI>
{
    public static ShippingCartI OnEvent(PurchasedCartI aggregatePayload, Event<PaymentCompletedI> ev) => throw new NotImplementedException();
    public ShippingCartI OnEventInstance(PurchasedCartI aggregatePayload, Event<PaymentCompletedI> ev) => OnEvent(aggregatePayload, ev);
}
