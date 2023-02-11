using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts.Commands;

public class ReceivePaymentToPurchasedCartR : IVersionValidationCommand<PurchasedCartR>
{
    public Guid CartId { get; init; }

    public string PaymentMethod { get; init; } = "Cash";
    public decimal Amount { get; init; } = 0;
    public string Currency { get; init; } = "JPY";
    public Guid GetAggregateId()
    {
        return CartId;
    }
    public int ReferenceVersion { get; init; }

    public class Handler : IVersionValidationCommandHandler<PurchasedCartR, ReceivePaymentToPurchasedCartR>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<PurchasedCartR>> HandleCommandAsync(
            Func<AggregateState<PurchasedCartR>> getAggregateState,
            ReceivePaymentToPurchasedCartR command)
        {
            await Task.CompletedTask;
            yield return new PaymentReceivedToCartR
                { PaymentMethod = command.PaymentMethod, Amount = command.Amount, Currency = command.Currency };
            yield return new PaymentCompletedR();
        }
    }
}
