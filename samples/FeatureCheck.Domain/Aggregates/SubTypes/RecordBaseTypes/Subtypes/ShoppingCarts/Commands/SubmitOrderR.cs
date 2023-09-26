using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts.Commands;

public record SubmitOrderR : IVersionValidationCommand<ShoppingCartR>
{
    public Guid CartId { get; init; } = Guid.Empty;
    public DateTime OrderSubmittedLocalTime { get; init; }
    public Guid GetAggregateId() => CartId;
    public int ReferenceVersion { get; init; }

    public class Handler : IVersionValidationCommandHandler<ShoppingCartR, SubmitOrderR>
    {

        public IEnumerable<IEventPayloadApplicableTo<ShoppingCartR>> HandleCommand(
            Func<AggregateState<ShoppingCartR>> getAggregateState,
            SubmitOrderR command)
        {
            yield return new OrderSubmittedR { OrderSubmittedLocalTime = command.OrderSubmittedLocalTime };
        }
    }
}
