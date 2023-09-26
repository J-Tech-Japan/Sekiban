using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Commands;

public record SubmitOrderI : IVersionValidationCommand<ShoppingCartI>
{
    public Guid CartId { get; init; } = Guid.Empty;
    public DateTime OrderSubmittedLocalTime { get; init; }
    public Guid GetAggregateId() => CartId;
    public int ReferenceVersion { get; init; }

    public class Handler : IVersionValidationCommandHandler<ShoppingCartI, SubmitOrderI>
    {

        public IEnumerable<IEventPayloadApplicableTo<ShoppingCartI>> HandleCommand(
            Func<AggregateState<ShoppingCartI>> getAggregateState,
            SubmitOrderI command)
        {
            yield return new OrderSubmittedI { OrderSubmittedLocalTime = command.OrderSubmittedLocalTime };
        }
    }
}
