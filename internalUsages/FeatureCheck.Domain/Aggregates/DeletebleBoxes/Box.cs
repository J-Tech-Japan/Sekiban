using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.DeletebleBoxes;

public record Box(string Code, string Name) : IDeletableAggregatePayload<Box>
{
    public static Box CreateInitialPayload(Box? _) => new(string.Empty, string.Empty);
    public bool IsDeleted { get; init; }
}
