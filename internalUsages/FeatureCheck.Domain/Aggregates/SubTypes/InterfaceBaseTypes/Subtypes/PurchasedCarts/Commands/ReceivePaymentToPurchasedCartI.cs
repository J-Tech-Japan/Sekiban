using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts.Commands;

public record ReceivePaymentToPurchasedCartI : ICommandWithVersionValidation<PurchasedCartI>
{
    public Guid CartId { get; init; }

    public string PaymentMethod { get; init; } = "Cash";
    public decimal Amount { get; init; } = 0;
    public string Currency { get; init; } = "JPY";

    public Guid GetAggregateId() => CartId;

    public int ReferenceVersion { get; init; }

    public class Handler : ICommandHandler<PurchasedCartI, ReceivePaymentToPurchasedCartI>
    {
        public IEnumerable<IEventPayloadApplicableTo<PurchasedCartI>> HandleCommand(
            ReceivePaymentToPurchasedCartI command,
            ICommandContext<PurchasedCartI> context)
        {
            yield return new PaymentReceivedToCartI
            {
                PaymentMethod = command.PaymentMethod, Amount = command.Amount, Currency = command.Currency
            };
            yield return new PaymentCompletedI();
        }
    }
}
