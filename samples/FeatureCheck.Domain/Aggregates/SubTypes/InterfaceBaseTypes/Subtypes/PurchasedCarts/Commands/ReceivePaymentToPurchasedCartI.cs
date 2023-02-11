using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts.Commands;

public record ReceivePaymentToPurchasedCartI : IVersionValidationCommand<PurchasedCartI>
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

    public class Handler : IVersionValidationCommandHandler<PurchasedCartI, ReceivePaymentToPurchasedCartI>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<PurchasedCartI>> HandleCommandAsync(
            Func<AggregateState<PurchasedCartI>> getAggregateState,
            ReceivePaymentToPurchasedCartI command)
        {
            await Task.CompletedTask;
            yield return new PaymentReceivedToCartI
                { PaymentMethod = command.PaymentMethod, Amount = command.Amount, Currency = command.Currency };
            yield return new PaymentCompletedI();
        }
    }
}
