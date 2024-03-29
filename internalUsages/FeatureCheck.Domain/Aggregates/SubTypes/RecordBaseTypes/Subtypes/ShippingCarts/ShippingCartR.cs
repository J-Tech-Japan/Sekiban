using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShippingCarts;

public record ShippingCartR : CartAggregateR, IAggregateSubtypePayload<CartAggregateR, ShippingCartR>
{
    public static ShippingCartR CreateInitialPayload(ShippingCartR? _) => new();
}
