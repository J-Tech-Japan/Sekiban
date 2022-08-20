using CustomerDomainContext.Aggregates.LoyaltyPoints.Consts;
namespace CustomerDomainContext.Aggregates.LoyaltyPoints.Events
{
    public record LoyaltyPointUsed(DateTime HappenedDate, LoyaltyPointUsageTypeKeys Reason, int PointAmount, string Note) : IChangedEventPayload;
}
