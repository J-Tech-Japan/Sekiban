namespace Sekiban.Dcb.Events;

/// <summary>
///     Input DTO from WASM client: payload bytes + event type name + tag strings.
///     Server generates EventId, SortableUniqueId, and EventMetadata.
/// </summary>
public record SerializableEventCandidate(
    byte[] Payload,
    string EventPayloadName,
    IReadOnlyList<string> Tags);
