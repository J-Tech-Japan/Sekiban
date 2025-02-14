using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Serialize;
using System.Text.Json;
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
        EventDocumentCommon common,
        JsonSerializerOptions serializeOptions);

    public ResultBox<string> SerializePayloadToJson(ISekibanSerializer serializer, IEvent ev);
    public void CheckEventJsonContextOption(JsonSerializerOptions options);
}