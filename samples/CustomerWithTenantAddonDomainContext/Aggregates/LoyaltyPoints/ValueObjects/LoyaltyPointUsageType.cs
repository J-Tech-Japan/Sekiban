using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.Consts;
namespace CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.ValueObjects;

public record LoyaltyPointUsageType : IValueObject<LoyaltyPointUsageTypeKeys>
{
    public static Dictionary<int, string> LoyaltyPointUsageTypes = new()
    {
        { (int)LoyaltyPointUsageTypeKeys.FlightDomestic, "国内線フライト" },
        { (int)LoyaltyPointUsageTypeKeys.FlightInternational, "国際線フライト" },
        { (int)LoyaltyPointUsageTypeKeys.FlightUpgrade, "フライトアップグレード" },
        { (int)LoyaltyPointUsageTypeKeys.TravelHotel, "ホテル利用" },
        { (int)LoyaltyPointUsageTypeKeys.TravelCarRental, "レンタカー" },
        { (int)LoyaltyPointUsageTypeKeys.PointExchange, "ポイント交換" },
        { (int)LoyaltyPointUsageTypeKeys.RestaurantCoupon, "レストラン利用券" }
    };
    public LoyaltyPointUsageType(LoyaltyPointUsageTypeKeys receiveType)
    {
        if (!Enum.IsDefined(typeof(LoyaltyPointUsageTypeKeys), receiveType))
        {
            throw new InvalidValueException("登録されていないポイント使用区分です。");
        }
        Value = receiveType;
    }
    public LoyaltyPointUsageTypeKeys Value { get; }

    public static implicit operator LoyaltyPointUsageTypeKeys(LoyaltyPointUsageType vo) =>
        vo.Value;
    public static implicit operator LoyaltyPointUsageType(LoyaltyPointUsageTypeKeys v) =>
        new(v);
}
