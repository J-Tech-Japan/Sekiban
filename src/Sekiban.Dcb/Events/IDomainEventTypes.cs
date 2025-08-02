using ResultBoxes;
using Sekiban.Dcb.Serialize;
using System.Text.Json;
namespace Sekiban.Dcb.Events;

public interface IDomainEventTypes
{
    public ResultBox<IDomainEvent> GenerateTypedEvent(
        IDomainEventPayload payload,
        string sortableUniqueId,
        int version,
        DomainEventMetadata metadata);

    public ResultBox<IDomainEvent> DeserializeToTyped(DomainEventDocumentCommon common, JsonSerializerOptions serializeOptions);

    public ResultBox<string> SerializePayloadToJson(ISekibanSerializer serializer, IDomainEvent ev);
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
