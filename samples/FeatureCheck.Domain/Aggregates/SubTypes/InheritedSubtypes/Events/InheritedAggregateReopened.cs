using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes.Events;

public record InheritedAggregateReopened(string Reason) : IEventPayload<ClosedSubAggregate, ProcessingSubAggregate, InheritedAggregateReopened>
{
    public static ProcessingSubAggregate OnEvent(ClosedSubAggregate aggregatePayload, Event<InheritedAggregateReopened> ev) => aggregatePayload;
}
