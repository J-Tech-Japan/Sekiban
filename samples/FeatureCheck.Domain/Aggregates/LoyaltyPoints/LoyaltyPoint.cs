using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints;

public record LoyaltyPoint(int CurrentPoint, DateTime? LastOccuredTime, bool IsDeleted) : IDeletableAggregatePayload
{
    public LoyaltyPoint() : this(0, null, false)
    {
    }
}
