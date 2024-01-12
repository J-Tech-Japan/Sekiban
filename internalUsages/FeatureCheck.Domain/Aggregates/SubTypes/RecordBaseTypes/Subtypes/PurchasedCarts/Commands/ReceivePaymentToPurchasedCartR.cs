using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts.Commands;

public class ReceivePaymentToPurchasedCartR : ICommandWithVersionValidation<PurchasedCartR>
{
    public Guid CartId { get; init; }

    public string PaymentMethod { get; init; } = "Cash";
    public decimal Amount { get; init; } = 0;
    public string Currency { get; init; } = "JPY";
    public Guid GetAggregateId() => CartId;
    public int ReferenceVersion { get; init; }

    public class Handler : ICommandHandler<PurchasedCartR, ReceivePaymentToPurchasedCartR>
    {
        public IEnumerable<IEventPayloadApplicableTo<PurchasedCartR>> HandleCommand(
            ReceivePaymentToPurchasedCartR command,
            ICommandContext<PurchasedCartR> context)
        {
            yield return new PaymentReceivedToCartR { PaymentMethod = command.PaymentMethod, Amount = command.Amount, Currency = command.Currency };
            yield return new PaymentCompletedR();
        }
    }
}
