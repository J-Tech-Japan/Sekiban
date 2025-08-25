namespace Sekiban.Dcb.Actors;

/// <summary>
///     Default implementation of IEventSubscriptionStatus
/// </summary>
public class EventSubscriptionStatus : IEventSubscriptionStatus
{

    public EventSubscriptionStatus(
        string subscriptionId,
        bool isActive = false,
        bool isPaused = false,
        string? currentPosition = null,
        DateTime? startedAt = null,
        DateTime? pausedAt = null,
        long eventsReceived = 0,
        long eventsProcessed = 0,
        long errorCount = 0,
        string? lastError = null,
        DateTime? lastErrorAt = null,
        DateTime? lastEventReceivedAt = null,
        DateTime? lastEventProcessedAt = null,
        double? averageProcessingTimeMs = null)
    {
        SubscriptionId = subscriptionId;
        IsActive = isActive;
        IsPaused = isPaused;
        CurrentPosition = currentPosition;
        StartedAt = startedAt;
        PausedAt = pausedAt;
        EventsReceived = eventsReceived;
        EventsProcessed = eventsProcessed;
        ErrorCount = errorCount;
        LastError = lastError;
        LastErrorAt = lastErrorAt;
        LastEventReceivedAt = lastEventReceivedAt;
        LastEventProcessedAt = lastEventProcessedAt;
        AverageProcessingTimeMs = averageProcessingTimeMs;
    }
    public string SubscriptionId { get; init; }
    public bool IsActive { get; init; }
    public bool IsPaused { get; init; }
    public string? CurrentPosition { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? PausedAt { get; init; }
    public long EventsReceived { get; init; }
    public long EventsProcessed { get; init; }
    public long ErrorCount { get; init; }
    public string? LastError { get; init; }
    public DateTime? LastErrorAt { get; init; }
    public DateTime? LastEventReceivedAt { get; init; }
    public DateTime? LastEventProcessedAt { get; init; }
    public double? AverageProcessingTimeMs { get; init; }
}
