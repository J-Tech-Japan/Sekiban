using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
/// Abstraction for tracking event delivery statistics for a multi-projection.
/// Implementations can either record detailed stats (for debugging) or be no-op (for production).
/// <para>
/// The MultiProjectionGrain passes events without deserializing their payloads: <see cref="Event.Payload" /> is a
/// placeholder. Only <see cref="Event.Id" />, <see cref="Event.EventType" />,
/// <see cref="Event.SortableUniqueIdValue" />, <see cref="Event.Tags" /> and <see cref="Event.EventMetadata" /> are
/// meaningful. Record methods may be called from catch-up callbacks running off the grain scheduler, so
/// implementations must be thread-safe.
/// </para>
/// </summary>
public interface IMultiProjectionEventStatistics
{
    /// <summary>
    /// Record a batch of events delivered via Orleans stream.
    /// </summary>
    void RecordStreamBatch(IEnumerable<Event> events);

    /// <summary>
    /// Record a batch of events delivered via EventStore catch-up.
    /// </summary>
    void RecordCatchUpBatch(IEnumerable<Event> events);

    /// <summary>
    /// Snapshot current statistics into tuple form to avoid cross-assembly dependencies.
    /// </summary>
    (int totalUnique,
     long totalDeliveries,
     long duplicateDeliveries,
     int eventsWithMultipleDeliveries,
     int maxDeliveryCount,
     double averageDeliveryCount,
     int streamUnique,
     long streamDeliveries,
     int catchUpUnique,
     long catchUpDeliveries,
     string? message) Snapshot();
}

