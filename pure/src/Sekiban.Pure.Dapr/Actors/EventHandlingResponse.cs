using System.Text.Json.Serialization;
namespace Sekiban.Pure.Dapr.Actors;

/// <summary>
///     Response from event handling
/// </summary>
public record EventHandlingResponse
{
    /// <summary>
    ///     Whether the event was handled successfully
    /// </summary>
    [JsonPropertyName("isSuccess")]
    public bool IsSuccess { get; init; }

    /// <summary>
    ///     Error details if handling failed
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    /// <summary>
    ///     The last processed event ID
    /// </summary>
    [JsonPropertyName("lastProcessedEventId")]
    public string? LastProcessedEventId { get; init; }

    /// <summary>
    ///     Creates a new EventHandlingResponse
    /// </summary>
    public EventHandlingResponse() { }

    /// <summary>
    ///     Creates a successful EventHandlingResponse
    /// </summary>
    public static EventHandlingResponse Success(string lastProcessedEventId) =>
        new()
        {
            IsSuccess = true,
            LastProcessedEventId = lastProcessedEventId
        };

    /// <summary>
    ///     Creates a failed EventHandlingResponse
    /// </summary>
    public static EventHandlingResponse Failure(string errorMessage) =>
        new()
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
}
