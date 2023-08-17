using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShippingCarts;

public record ShippingCartR : CartAggregateR, IAggregateSubtypePayload<CartAggregateR>
{
    public static IAggregatePayloadCommon CreateInitialPayload() => new ShippingCartR();
}
