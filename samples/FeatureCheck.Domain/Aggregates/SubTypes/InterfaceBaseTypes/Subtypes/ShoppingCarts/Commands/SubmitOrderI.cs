using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Commands;

public record SubmitOrderI : IVersionValidationCommand<ShoppingCartI>
{
    public Guid CartId { get; init; } = Guid.Empty;
    public DateTime OrderSubmittedLocalTime { get; init; }
    public Guid GetAggregateId()
    {
        return CartId;
    }
    public int ReferenceVersion
    {
        get;
        init;
    }

    public class Handler : IVersionValidationCommandHandler<ShoppingCartI, SubmitOrderI>
    {

        public async IAsyncEnumerable<IEventPayloadApplicableTo<ShoppingCartI>> HandleCommandAsync(
            Func<AggregateState<ShoppingCartI>> getAggregateState,
            SubmitOrderI command)
        {
            await Task.CompletedTask;
            yield return new OrderSubmittedI
            {
                OrderSubmittedLocalTime = command.OrderSubmittedLocalTime
            };
        }
    }
}