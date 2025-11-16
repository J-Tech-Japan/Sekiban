using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
/// Abstraction for tracking event delivery statistics for a multi-projection.
/// Implementations can either record detailed stats (for debugging) or be no-op (for production).
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

