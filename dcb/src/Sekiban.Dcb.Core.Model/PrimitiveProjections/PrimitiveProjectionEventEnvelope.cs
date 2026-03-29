namespace Sekiban.Dcb.Primitives;

/// <summary>
///     Serializable event data passed to primitive runtimes for batched application.
/// </summary>
public readonly record struct PrimitiveProjectionEventEnvelope(
    string EventType,
    string EventPayloadJson,
    IReadOnlyList<string> Tags,
    string? SortableUniqueId);
