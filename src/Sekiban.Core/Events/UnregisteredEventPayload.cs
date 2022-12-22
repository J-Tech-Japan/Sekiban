namespace Sekiban.Core.Events;

public record UnregisteredEventPayload : IEventPayloadCommon
{
    public string JsonString { get; init; } = string.Empty;
    public string EventTypeName { get; init; } = string.Empty;
}
