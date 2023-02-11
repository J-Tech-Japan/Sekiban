using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts;
using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes;

public record CartAggregateR : IParentAggregatePayload<CartAggregateR, ShoppingCartR>
{
    public ImmutableSortedDictionary<int, CartItemRecordR> Items { get; init; } = ImmutableSortedDictionary<int, CartItemRecordR>.Empty;
}
