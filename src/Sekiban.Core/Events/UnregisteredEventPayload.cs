namespace Sekiban.Core.Events;

/// <summary>
///     Unregisterd Event Types.
///     This is uses when a event is saved but some of the other server
///     code is old and could not handle events. it will save
/// </summary>
public record UnregisteredEventPayload : IEventPayloadCommon
{

    /// <summary>
    ///     payload converted to JSON
    /// </summary>
    public string JsonString { get; init; } = string.Empty;
    /// <summary>
    ///     Original Event Payload name
    /// </summary>
    public string EventTypeName { get; init; } = string.Empty;
}
