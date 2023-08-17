using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.ALotOfEvents;

public record ALotOfEventsAggregate : IAggregatePayload
{
    public int Count { get; init; }
    public static IAggregatePayloadCommon CreateInitialPayload() => new ALotOfEventsAggregate();
}
