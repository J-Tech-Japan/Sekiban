namespace Sekiban.Dcb.ColdEvents;

public record ColdSegmentInfo(
    string Path,
    string FromSortableUniqueId,
    string ToSortableUniqueId,
    int EventCount,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CreatedAtUtc);
