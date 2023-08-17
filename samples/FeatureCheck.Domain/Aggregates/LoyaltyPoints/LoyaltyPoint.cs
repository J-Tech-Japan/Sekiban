using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints;

public record LoyaltyPoint(int CurrentPoint, DateTime? LastOccuredTime, bool IsDeleted) : IDeletableAggregatePayload
{
    public static IAggregatePayloadCommon CreateInitialPayload() => new LoyaltyPoint(0, null, false);
}
