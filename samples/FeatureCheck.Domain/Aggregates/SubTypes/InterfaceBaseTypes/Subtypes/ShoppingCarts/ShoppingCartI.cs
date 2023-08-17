using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts;

public record ShoppingCartI : ICartAggregate, IAggregateSubtypePayload<ICartAggregate>
{
    public static IAggregatePayloadCommon CreateInitialPayload() => new ShoppingCartI();
    public ImmutableSortedDictionary<int, CartItemRecordI> Items { get; init; } = ImmutableSortedDictionary<int, CartItemRecordI>.Empty;
}
