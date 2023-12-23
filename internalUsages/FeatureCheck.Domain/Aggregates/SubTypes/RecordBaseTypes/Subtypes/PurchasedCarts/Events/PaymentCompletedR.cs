using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShippingCarts;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts.Events;

public record PaymentCompletedR : IEventPayload<PurchasedCartR, ShippingCartR, PaymentCompletedR>
{
    public static ShippingCartR OnEvent(PurchasedCartR aggregatePayload, Event<PaymentCompletedR> ev) => new() { Items = aggregatePayload.Items };
}
