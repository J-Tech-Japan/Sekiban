using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts;

public record PurchasedCartR : CartAggregateR, IAggregateSubtypePayload<CartAggregateR>
{
    public DateTime PurchasedDate { get; init; } = DateTime.MinValue;

    public ImmutableList<PaymentRecordR> Payments { get; init; } = ImmutableList<PaymentRecordR>.Empty;
}
