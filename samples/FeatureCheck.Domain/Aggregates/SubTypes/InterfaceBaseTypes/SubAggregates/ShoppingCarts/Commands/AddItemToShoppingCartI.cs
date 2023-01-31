using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.ShoppingCarts.Commands;

public class AddItemToShoppingCartI : ICommand<ShoppingCartI>
{
    public Guid CartId { get; init; } = Guid.Empty;
    [Required]
    public string Code { get; init; } = string.Empty;
    [Required]
    public string Name { get; init; } = string.Empty;
    [Range(1, 1000)]
    public int Quantity { get; init; } = 0;
    public Guid GetAggregateId() => CartId;

    public class Handler : ICommandHandler<ShoppingCartI, AddItemToShoppingCartI>
    {

        public async IAsyncEnumerable<IEventPayloadApplicableTo<ShoppingCartI>> HandleCommandAsync(
            Func<AggregateState<ShoppingCartI>> getAggregateState,
            AddItemToShoppingCartI command)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
