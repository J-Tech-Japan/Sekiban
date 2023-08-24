using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShippingCarts;

public class ShippingCartI : ICartAggregate, IAggregateSubtypePayload<ICartAggregate, ShippingCartI>
{
    public static ShippingCartI CreateInitialPayload(ShippingCartI? _) => new();
    public ImmutableSortedDictionary<int, CartItemRecordI> Items { get; init; } = ImmutableSortedDictionary<int, CartItemRecordI>.Empty;
}
