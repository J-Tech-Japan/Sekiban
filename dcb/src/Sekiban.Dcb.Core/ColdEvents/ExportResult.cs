namespace Sekiban.Dcb.ColdEvents;

public record ExportResult(
    int ExportedEventCount,
    IReadOnlyList<ColdSegmentInfo> NewSegments,
    string UpdatedManifestVersion,
    string? Reason = null,
    string? ReasonDetail = null,
    bool ShouldContinueWithinCycle = false);
