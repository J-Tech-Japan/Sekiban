using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Exception;
namespace Sekiban.Pure.Events;

public class EmptyEventTypes : IEventTypes
{
    public ResultBox<IEvent> GenerateTypedEvent(
        IEventPayload payload,
        PartitionKeys partitionKeys,
        string sortableUniqueId,
        int version) => ResultBox<IEvent>.FromException(new SekibanEventTypeNotFoundException(""));
}
