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

    public ResultBox<IEventDocument> ConvertToEventDocument(IEvent ev);

    public ResultBox<IEvent> DeserializeToTyped(EventDocumentCommon common, JsonSerializerOptions serializeOptions);

    public ResultBox<string> SerializePayloadToJson(ISekibanSerializer serializer, IEvent ev);
    public void CheckEventJsonContextOption(JsonSerializerOptions options);

    /// <summary>
    ///     Gets a list of all event types that implement IEventPayload
    /// </summary>
    /// <returns>List of event types</returns>
    List<Type> GetEventTypes();
    
    /// <summary>
    /// Gets the type from the event type name.
    /// </summary>
    /// <param name="eventTypeName">Name of the event type</param>
    /// <returns>The found type, or null if not found</returns>
    Type? GetEventTypeByName(string eventTypeName);
}
