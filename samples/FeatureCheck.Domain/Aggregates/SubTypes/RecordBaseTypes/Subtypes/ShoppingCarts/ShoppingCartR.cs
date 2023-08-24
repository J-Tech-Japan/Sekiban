using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts;

public record ShoppingCartR : CartAggregateR, IAggregateSubtypePayload<CartAggregateR, ShoppingCartR>
{
    public static ShoppingCartR CreateInitialPayload(ShoppingCartR? _) => new();
}
