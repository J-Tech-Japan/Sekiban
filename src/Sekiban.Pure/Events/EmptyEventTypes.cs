using System.Text.Json;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Exceptions;

namespace Sekiban.Pure.Events;

public class EmptyEventTypes : IEventTypes
{
    public ResultBox<IEvent> GenerateTypedEvent(
        IEventPayload payload,
        PartitionKeys partitionKeys,
        string sortableUniqueId,
        int version, EventMetadata metadata)
    {
        return ResultBox<IEvent>.FromException(new SekibanEventTypeNotFoundException(""));
    }

    public ResultBox<IEventDocument> ConvertToEventDocument(IEvent ev)
    {
        return ResultBox<IEventDocument>.FromException(new SekibanEventTypeNotFoundException(""));
    }

    public ResultBox<IEvent> DeserializeToTyped(EventDocumentCommon common, JsonSerializerOptions serializeOptions)
    {
        return ResultBox<IEvent>.FromException(new SekibanEventTypeNotFoundException(""));
    }

    public void CheckEventJsonContextOption(JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}