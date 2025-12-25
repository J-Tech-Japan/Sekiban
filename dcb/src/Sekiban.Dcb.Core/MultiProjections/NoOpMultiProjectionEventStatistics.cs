using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
/// Production implementation that does nothing and reports a not-recording message.
/// </summary>
public class NoOpMultiProjectionEventStatistics : IMultiProjectionEventStatistics
{
    public void RecordStreamBatch(IEnumerable<Event> events) { }
    public void RecordCatchUpBatch(IEnumerable<Event> events) { }

    public (int totalUnique, long totalDeliveries, long duplicateDeliveries, int eventsWithMultipleDeliveries, int maxDeliveryCount, double averageDeliveryCount, int streamUnique, long streamDeliveries, int catchUpUnique, long catchUpDeliveries, string? message) Snapshot()
        => (0, 0, 0, 0, 0, 0d, 0, 0, 0, 0, "EventStatistic Not Recording");
}

