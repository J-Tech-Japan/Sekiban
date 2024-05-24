namespace FeatureCheck.Domain.Aggregates.Branches;

public record BranchId(Guid Value)
{
    public static BranchId FromValue(Guid value) => new(value);
}
