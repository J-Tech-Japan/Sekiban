using Sekiban.Core.Aggregate;
namespace Customer.Domain.Aggregates.LoyaltyPoints;

public record LoyaltyPointPayload : IAggregatePayload
{
    public DateTime? LastOccuredTime { get; init; }
    public int CurrentPoint { get; init; }
    public LoyaltyPointPayload(int currentPoint, DateTime? lastOccuredTime)
    {
        CurrentPoint = currentPoint;
        LastOccuredTime = lastOccuredTime;
    }
    public LoyaltyPointPayload() { }
}
