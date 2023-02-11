using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts.Events;

public record PaymentReceivedToCartI : IEventPayload<PurchasedCartI, PaymentReceivedToCartI>
{
    public string PaymentMethod { get; init; } = "Cash";
    public decimal Amount { get; init; } = 0;
    public string Currency { get; init; } = "JPY";
    public PurchasedCartI OnEventInstance(PurchasedCartI aggregatePayload, Event<PaymentReceivedToCartI> ev)
    {
        return OnEvent(aggregatePayload, ev);
    }
    public static PurchasedCartI OnEvent(PurchasedCartI aggregatePayload, Event<PaymentReceivedToCartI> ev)
    {
        return aggregatePayload with
        {
            Payments = aggregatePayload.Payments.Add(
                new PaymentRecord
                    { PaymentMethod = ev.Payload.PaymentMethod, Amount = ev.Payload.Amount, Currency = ev.Payload.Currency })
        };
    }
}
