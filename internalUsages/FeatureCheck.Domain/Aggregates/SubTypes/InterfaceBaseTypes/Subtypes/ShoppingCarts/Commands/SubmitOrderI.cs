using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Commands;

public record SubmitOrderI : ICommandWithVersionValidation<ShoppingCartI>
{
    public Guid CartId { get; init; } = Guid.Empty;
    public DateTime OrderSubmittedLocalTime { get; init; }

    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId() => CartId;

    public class Handler : ICommandHandler<ShoppingCartI, SubmitOrderI>
    {
        public IEnumerable<IEventPayloadApplicableTo<ShoppingCartI>> HandleCommand(
            SubmitOrderI command,
            ICommandContext<ShoppingCartI> context)
        {
            yield return new OrderSubmittedI
            {
                OrderSubmittedLocalTime = command.OrderSubmittedLocalTime
            };
        }
        public Guid SpecifyAggregateId(SubmitOrderI command) => command.CartId;
    }
}
