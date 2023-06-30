using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.ValueObjects;

public record LoyaltyPointUsageType : IValueObject<LoyaltyPointUsageTypeKeys>
{
    public static Dictionary<int, string> LoyaltyPointUsageTypes = new()
    {
        { (int)LoyaltyPointUsageTypeKeys.FlightDomestic, "Domestic Flight" },
        { (int)LoyaltyPointUsageTypeKeys.FlightInternational, "International Flight" },
        { (int)LoyaltyPointUsageTypeKeys.FlightUpgrade, "Upgrading Flight" },
        { (int)LoyaltyPointUsageTypeKeys.TravelHotel, "Hotel Stay" },
        { (int)LoyaltyPointUsageTypeKeys.TravelCarRental, "Car Rental" },
        { (int)LoyaltyPointUsageTypeKeys.PointExchange, "Point Exchange" },
        { (int)LoyaltyPointUsageTypeKeys.RestaurantCoupon, "Restaurant Usages" }
    };

    public LoyaltyPointUsageType(LoyaltyPointUsageTypeKeys value)
    {
        if (!Enum.IsDefined(typeof(LoyaltyPointUsageTypeKeys), value))
        {
            throw new InvalidValueException("It's an unregistered point earning category.");
        }
        Value = value;
    }

    public LoyaltyPointUsageTypeKeys Value { get; }

    public static implicit operator LoyaltyPointUsageTypeKeys(LoyaltyPointUsageType vo) => vo.Value;

    public static implicit operator LoyaltyPointUsageType(LoyaltyPointUsageTypeKeys v) => new(v);
}
