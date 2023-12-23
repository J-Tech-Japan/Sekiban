namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes;

public record CartItemRecordI
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; } = 0;
}
