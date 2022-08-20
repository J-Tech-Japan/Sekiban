namespace CustomerDomainContext.Aggregates.LoyaltyPoints
{
    public record LoyaltyPointContents : IAggregateContents
    {
        public DateTime? LastOccuredTime { get; init; }
        public int CurrentPoint { get; init; }
        public LoyaltyPointContents(int currentPoint, DateTime? lastOccuredTime)
        {
            CurrentPoint = currentPoint;
            LastOccuredTime = lastOccuredTime;
        }
        public LoyaltyPointContents() { }
    }
}
