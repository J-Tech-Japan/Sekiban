using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes;

public record InheritedAggregateClosed(string Reason) : IEventPayload<ProcessingSubAggregate, ClosedSubAggregate, InheritedAggregateClosed>
{
    public static ClosedSubAggregate OnEvent(ProcessingSubAggregate aggregatePayload, Event<InheritedAggregateClosed> ev) =>
        new() { YearMonth = aggregatePayload.YearMonth };
}
