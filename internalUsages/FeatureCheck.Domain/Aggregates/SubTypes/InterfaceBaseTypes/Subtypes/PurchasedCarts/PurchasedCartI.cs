using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts;

public record PurchasedCartI : ICartAggregate, IAggregateSubtypePayload<ICartAggregate, PurchasedCartI>
{
    public DateTime PurchasedDate { get; init; } = DateTime.MinValue;

    public ImmutableList<PaymentRecord> Payments { get; init; } = ImmutableList<PaymentRecord>.Empty;
    public static PurchasedCartI CreateInitialPayload(PurchasedCartI? _) => new();
    public ImmutableSortedDictionary<int, CartItemRecordI> Items { get; init; } = ImmutableSortedDictionary<int, CartItemRecordI>.Empty;
}
