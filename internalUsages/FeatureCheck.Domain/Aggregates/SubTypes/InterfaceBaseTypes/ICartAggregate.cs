using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts;
using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes;

public interface ICartAggregate : IParentAggregatePayload<ICartAggregate, ShoppingCartI>
{
    public ImmutableSortedDictionary<int, CartItemRecordI> Items { get; init; }
}
