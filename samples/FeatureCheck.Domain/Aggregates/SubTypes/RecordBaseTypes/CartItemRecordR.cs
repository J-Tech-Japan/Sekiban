namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes;

public record CartItemRecordR
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; } = 0;
}
