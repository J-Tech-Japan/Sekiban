using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts.Events;

public record PaymentReceivedToCartR : IEventPayload<PurchasedCartR, PaymentReceivedToCartR>
{
    public string PaymentMethod { get; init; } = "Cash";
    public decimal Amount { get; init; } = 0;
    public string Currency { get; init; } = "JPY";
    public PurchasedCartR OnEventInstance(PurchasedCartR aggregatePayload, Event<PaymentReceivedToCartR> ev) => OnEvent(aggregatePayload, ev);
    public static PurchasedCartR OnEvent(PurchasedCartR aggregatePayload, Event<PaymentReceivedToCartR> ev) =>
        aggregatePayload with
        {
            Payments = aggregatePayload.Payments.Add(
                new PaymentRecordR { PaymentMethod = ev.Payload.PaymentMethod, Amount = ev.Payload.Amount, Currency = ev.Payload.Currency })
        };
}
