using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts.Commands;

public record AddItemToShoppingCartR : ICommand<ShoppingCartR>
{
    public Guid CartId { get; init; } = Guid.Empty;
    [Required]
    public string Code { get; init; } = string.Empty;
    [Required]
    public string Name { get; init; } = string.Empty;
    [Range(1, 1000)]
    public int Quantity { get; init; } = 0;
    public Guid GetAggregateId()
    {
        return CartId;
    }

    public class Handler : ICommandHandler<ShoppingCartR, AddItemToShoppingCartR>
    {

        public async IAsyncEnumerable<IEventPayloadApplicableTo<ShoppingCartR>> HandleCommandAsync(
            Func<AggregateState<ShoppingCartR>> getAggregateState,
            AddItemToShoppingCartR command)
        {
            await Task.CompletedTask;
            yield return new ItemAddedToShoppingCartR
            {
                Code = command.Code,
                Name = command.Name,
                Quantity = command.Quantity
            };
        }
    }
}