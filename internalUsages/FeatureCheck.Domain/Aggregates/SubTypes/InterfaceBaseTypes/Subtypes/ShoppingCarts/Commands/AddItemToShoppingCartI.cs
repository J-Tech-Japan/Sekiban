using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Commands;

public class AddItemToShoppingCartI : ICommand<ShoppingCartI>
{
    public Guid CartId { get; init; } = Guid.Empty;

    [Required]
    public string Code { get; init; } = string.Empty;

    [Required]
    public string Name { get; init; } = string.Empty;

    [Range(1, 1000)]
    public int Quantity { get; init; } = 0;

    public class Handler : ICommandHandler<ShoppingCartI, AddItemToShoppingCartI>
    {
        public IEnumerable<IEventPayloadApplicableTo<ShoppingCartI>> HandleCommand(
            AddItemToShoppingCartI command,
            ICommandContext<ShoppingCartI> context)
        {
            yield return new ItemAddedToShoppingCartI
            {
                Code = command.Code, Name = command.Name, Quantity = command.Quantity
            };
        }
        public Guid SpecifyAggregateId(AddItemToShoppingCartI command) => command.CartId;
    }
}
