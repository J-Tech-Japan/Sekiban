using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Exceptions;
using Sekiban.Pure.Serialize;
using System.Text.Json;
namespace Sekiban.Pure.Events;

public class EmptyEventTypes : IEventTypes
{
    public ResultBox<IEvent> GenerateTypedEvent(
        IEventPayload payload,
        PartitionKeys partitionKeys,
        string sortableUniqueId,
        int version,
        EventMetadata metadata) =>
        ResultBox<IEvent>.FromException(new SekibanEventTypeNotFoundException(""));

    public ResultBox<IEventDocument> ConvertToEventDocument(IEvent ev) =>
        ResultBox<IEventDocument>.FromException(new SekibanEventTypeNotFoundException(""));

    public ResultBox<IEvent> DeserializeToTyped(EventDocumentCommon common, JsonSerializerOptions serializeOptions) =>
        ResultBox<IEvent>.FromException(new SekibanEventTypeNotFoundException(""));
    public ResultBox<string> SerializePayloadToJson(ISekibanSerializer serializer, IEvent ev) =>
        throw new NotImplementedException();

    public void CheckEventJsonContextOption(JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}