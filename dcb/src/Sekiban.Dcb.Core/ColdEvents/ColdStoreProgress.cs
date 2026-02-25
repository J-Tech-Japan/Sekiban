namespace Sekiban.Dcb.ColdEvents;

public record ColdStoreProgress(
    string ServiceId,
    string? LatestSafeSortableUniqueId,
    string? LatestExportedSortableUniqueId,
    string? NextSinceSortableUniqueId,
    DateTimeOffset? LastExportedAtUtc,
    string ManifestVersion);
