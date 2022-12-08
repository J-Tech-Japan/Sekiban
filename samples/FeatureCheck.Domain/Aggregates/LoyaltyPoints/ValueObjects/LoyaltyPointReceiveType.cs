using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.ValueObjects;

public record LoyaltyPointReceiveType : IValueObject<LoyaltyPointReceiveTypeKeys>
{
    public static Dictionary<int, string> LoyaltyPointReceiveTypes = new()
    {
        { (int)LoyaltyPointReceiveTypeKeys.FlightDomestic, "国内線フライト" },
        { (int)LoyaltyPointReceiveTypeKeys.FlightInternational, "国際線フライト" },
        { (int)LoyaltyPointReceiveTypeKeys.TravelPoint, "旅行ポイント" },
        { (int)LoyaltyPointReceiveTypeKeys.CreditcardUsage, "クレジットカードポイント" },
        { (int)LoyaltyPointReceiveTypeKeys.InsuranceUsage, "保険ポイント" }
    };

    public LoyaltyPointReceiveType(LoyaltyPointReceiveTypeKeys value)
    {
        if (!Enum.IsDefined(typeof(LoyaltyPointReceiveTypeKeys), value))
        {
            throw new InvalidValueException("登録されていないポイント獲得区分です。");
        }
        Value = value;
    }

    public LoyaltyPointReceiveTypeKeys Value { get; }

    public static implicit operator LoyaltyPointReceiveTypeKeys(LoyaltyPointReceiveType vo) => vo.Value;

    public static implicit operator LoyaltyPointReceiveType(LoyaltyPointReceiveTypeKeys v) => new LoyaltyPointReceiveType(v);
}
