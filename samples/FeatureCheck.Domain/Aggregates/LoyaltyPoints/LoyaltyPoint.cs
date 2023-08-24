using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints;

public record LoyaltyPoint(int CurrentPoint, DateTime? LastOccuredTime, bool IsDeleted) : IDeletableAggregatePayload<LoyaltyPoint>
{
    public static LoyaltyPoint CreateInitialPayload(LoyaltyPoint? _) => new(0, null, false);
}
