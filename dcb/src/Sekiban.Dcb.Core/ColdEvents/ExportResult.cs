namespace Sekiban.Dcb.ColdEvents;

public record ExportResult(
    int ExportedEventCount,
    IReadOnlyList<ColdSegmentInfo> NewSegments,
    string UpdatedManifestVersion);
