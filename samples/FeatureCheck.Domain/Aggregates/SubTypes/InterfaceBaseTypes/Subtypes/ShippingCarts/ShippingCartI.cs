using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShippingCarts;

public class ShippingCartI : ICartAggregate, IAggregateSubtypePayload<ICartAggregate>
{
    public static IAggregatePayloadCommon CreateInitialPayload() => new ShippingCartI();

    public ImmutableSortedDictionary<int, CartItemRecordI> Items { get; init; } = ImmutableSortedDictionary<int, CartItemRecordI>.Empty;
}
