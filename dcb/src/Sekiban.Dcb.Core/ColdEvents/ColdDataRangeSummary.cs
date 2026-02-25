namespace Sekiban.Dcb.ColdEvents;

public record ColdDataRangeSummary(
    string ServiceId,
    string? OldestSortableUniqueId,
    string? LatestSortableUniqueId,
    int TotalEventCount,
    int SegmentCount,
    IReadOnlyList<ColdSegmentSummary> Segments);
