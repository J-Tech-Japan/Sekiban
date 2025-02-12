using System.Text.Json;
using ResultBoxes;
using Sekiban.Pure.Documents;

namespace Sekiban.Pure.Events;

public interface IEventTypes
{
    public ResultBox<IEvent> GenerateTypedEvent(
        IEventPayload payload,
        PartitionKeys partitionKeys,
        string sortableUniqueId,
        int version,
      EventMetadata metadata);

    public ResultBox<IEventDocument> ConvertToEventDocument(
        IEvent ev);

    public ResultBox<IEvent> DeserializeToTyped(
        EventDocumentCommon common, JsonSerializerOptions serializeOptions);

    public void CheckEventJsonContextOption(JsonSerializerOptions options);
}
