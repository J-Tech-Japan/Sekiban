using ResultBoxes;
using Sekiban.Pure.Documents;
namespace Sekiban.Pure.Events;

public interface IEventTypes
{
    public ResultBox<IEvent> GenerateTypedEvent(
        IEventPayload payload,
        PartitionKeys partitionKeys,
        string sortableUniqueId,
        int version);
}
