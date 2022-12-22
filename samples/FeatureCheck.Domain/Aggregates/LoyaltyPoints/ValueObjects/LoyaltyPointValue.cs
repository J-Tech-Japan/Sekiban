namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.ValueObjects;

public record LoyaltyPointValue
{
    public LoyaltyPointValue(int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Loyalty point value cannot be negative.");
        }
        Value = value;
    }
    public int Value { get; init; }
    public static implicit operator int(LoyaltyPointValue vo) => vo.Value;

    public static implicit operator LoyaltyPointValue(int v) => new(v);
}
