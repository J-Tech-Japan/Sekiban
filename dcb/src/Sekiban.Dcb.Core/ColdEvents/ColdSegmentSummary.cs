namespace Sekiban.Dcb.ColdEvents;

public record ColdSegmentSummary(
    string Path,
    string FromSortableUniqueId,
    string ToSortableUniqueId,
    int EventCount);
