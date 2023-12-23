using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.ValueObjects;

public record LoyaltyPointReceiveType : IValueObject<LoyaltyPointReceiveTypeKeys>
{
    public static Dictionary<int, string> LoyaltyPointReceiveTypes = new()
    {
        { (int)LoyaltyPointReceiveTypeKeys.FlightDomestic, "Domestic Flight" },
        { (int)LoyaltyPointReceiveTypeKeys.FlightInternational, "International Flight" },
        { (int)LoyaltyPointReceiveTypeKeys.TravelPoint, "Travel Point" },
        { (int)LoyaltyPointReceiveTypeKeys.CreditcardUsage, "Credit Card Point" },
        { (int)LoyaltyPointReceiveTypeKeys.InsuranceUsage, "Insurance Usage Point" }
    };

    public LoyaltyPointReceiveType(LoyaltyPointReceiveTypeKeys value)
    {
        if (!Enum.IsDefined(typeof(LoyaltyPointReceiveTypeKeys), value))
        {
            throw new InvalidValueException("It's an unregistered point earning category.");
        }
        Value = value;
    }

    public LoyaltyPointReceiveTypeKeys Value { get; }

    public static implicit operator LoyaltyPointReceiveTypeKeys(LoyaltyPointReceiveType vo) => vo.Value;

    public static implicit operator LoyaltyPointReceiveType(LoyaltyPointReceiveTypeKeys v) => new(v);
}
