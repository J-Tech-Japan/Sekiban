using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Event;

public interface IChangedEvent<TAggregatePayload> : IChangedEventPayload, IApplicableEvent<TAggregatePayload>
    where TAggregatePayload : IAggregatePayload
{
}
