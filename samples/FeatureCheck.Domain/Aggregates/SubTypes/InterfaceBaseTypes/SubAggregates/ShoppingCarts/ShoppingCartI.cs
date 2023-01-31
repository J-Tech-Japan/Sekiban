using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.ShoppingCarts;

public record ShoppingCartI : ICartAggregate, IAggregateSubtypePayload<ICartAggregate>
{
    public ImmutableSortedDictionary<int, CartItemRecordI> Items
    {
        get;
        init;
    } = ImmutableSortedDictionary<int, CartItemRecordI>.Empty;
}
