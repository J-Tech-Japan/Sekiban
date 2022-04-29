namespace CustomerDomainContext.Aggregates.LoyaltyPoints;

public record LoyaltyPointDto : AggregateDtoBase
{
    public int CurrentPoint { get; init; }

    public LoyaltyPointDto() { }

    public LoyaltyPointDto(LoyaltyPoint aggregate) : base(aggregate) { }
}
