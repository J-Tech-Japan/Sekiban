using Sekiban.Dcb.Common;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Statistics about events processed
/// </summary>
public record EventProviderStatistics(
    long TotalEventsProcessed,
    long SafeEventsProcessed,
    long UnsafeEventsProcessed,
    DateTime? LastEventTime,
    SortableUniqueId? LastEventPosition,
    TimeSpan? CatchUpDuration
);