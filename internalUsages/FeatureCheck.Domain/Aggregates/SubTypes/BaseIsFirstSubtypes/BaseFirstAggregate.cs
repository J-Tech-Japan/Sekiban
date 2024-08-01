using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes;

public record BaseFirstAggregate : IParentAggregatePayload<BaseFirstAggregate>
{
    public string Name { get; init; } = string.Empty;
    public long Price { get; init; } = 0;

    public static BaseFirstAggregate CreateInitialPayload(BaseFirstAggregate? _) => new();
}
