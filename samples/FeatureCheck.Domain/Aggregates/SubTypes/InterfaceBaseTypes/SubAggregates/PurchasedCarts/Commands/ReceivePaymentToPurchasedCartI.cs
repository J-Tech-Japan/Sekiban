using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.PurchasedCarts.Commands;

public record ReceivePaymentToPurchasedCartI : IVersionValidationCommand<PurchasedCartI>
{
    public Guid CartId { get; init; }

    public Guid GetAggregateId() => CartId;
    public int ReferenceVersion { get; init; }

    public class Handler : IVersionValidationCommandHandler<PurchasedCartI, ReceivePaymentToPurchasedCartI>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<PurchasedCartI>> HandleCommandAsync(
            Func<AggregateState<PurchasedCartI>> getAggregateState,
            ReceivePaymentToPurchasedCartI command)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
