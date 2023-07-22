using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes;

public record InheritedAggregateOpened(int YearMonth) : IEventPayload<ProcessingSubAggregate, InheritedAggregateOpened>
{
    public static ProcessingSubAggregate OnEvent(ProcessingSubAggregate aggregatePayload, Event<InheritedAggregateOpened> ev) =>
        new() { YearMonth = ev.Payload.YearMonth };
}
