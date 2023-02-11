using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts.Commands;

public record SubmitOrderR : IVersionValidationCommand<ShoppingCartR>
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

    public class Handler : IVersionValidationCommandHandler<ShoppingCartR, SubmitOrderR>
    {

        public async IAsyncEnumerable<IEventPayloadApplicableTo<ShoppingCartR>> HandleCommandAsync(
            Func<AggregateState<ShoppingCartR>> getAggregateState,
            SubmitOrderR command)
        {
            await Task.CompletedTask;
            yield return new OrderSubmittedR
            {
                OrderSubmittedLocalTime = command.OrderSubmittedLocalTime
            };
        }
    }
}
