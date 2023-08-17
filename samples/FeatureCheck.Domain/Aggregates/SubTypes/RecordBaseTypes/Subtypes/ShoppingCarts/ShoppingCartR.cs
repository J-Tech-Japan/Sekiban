using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts;

public record ShoppingCartR : CartAggregateR, IAggregateSubtypePayload<CartAggregateR>
{
    public static IAggregatePayloadCommon CreateInitialPayload() => new ShoppingCartR();
}
