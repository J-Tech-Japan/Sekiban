using Sekiban.Dcb.Actors;

namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
/// Implementation of IEventSubscriptionStatus for Orleans subscriptions
/// </summary>
public class OrleansEventSubscriptionStatus : IEventSubscriptionStatus
{
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

    public OrleansEventSubscriptionStatus(
        string subscriptionId,
        bool isActive,
        bool isPaused,
        string? currentPosition,
        DateTime? startedAt,
        DateTime? pausedAt,
        long eventsReceived,
        long eventsProcessed,
        long errorCount,
        string? lastError,
        DateTime? lastErrorAt,
        DateTime? lastEventReceivedAt,
        DateTime? lastEventProcessedAt,
        double? averageProcessingTimeMs)
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
}