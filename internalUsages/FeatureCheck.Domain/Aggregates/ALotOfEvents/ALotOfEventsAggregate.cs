using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.ALotOfEvents;

public record ALotOfEventsAggregate : IAggregatePayload<ALotOfEventsAggregate>
{
    public int Count { get; init; }

    public static ALotOfEventsAggregate CreateInitialPayload(ALotOfEventsAggregate? _) => new();
}
