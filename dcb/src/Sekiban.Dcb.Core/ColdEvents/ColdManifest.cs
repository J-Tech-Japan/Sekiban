namespace Sekiban.Dcb.ColdEvents;

public record ColdManifest(
    string ServiceId,
    string ManifestVersion,
    string? LatestSafeSortableUniqueId,
    IReadOnlyList<ColdSegmentInfo> Segments,
    DateTimeOffset UpdatedAtUtc);
