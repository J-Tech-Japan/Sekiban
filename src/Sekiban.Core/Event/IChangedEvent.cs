using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Event;

public interface IChangedEvent<TAggregatePayload> : IChangedAggregateEventPayload<TAggregatePayload>, IApplicableEvent<TAggregatePayload>
    where TAggregatePayload : IAggregatePayload
{
}
