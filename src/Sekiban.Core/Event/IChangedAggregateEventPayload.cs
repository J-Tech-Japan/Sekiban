using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Event;

public interface IChangedAggregateEventPayload<TAggregatePayload> : IChangedEventPayload, IApplicableEvent<TAggregatePayload>
    where TAggregatePayload : IAggregatePayload
{
}
