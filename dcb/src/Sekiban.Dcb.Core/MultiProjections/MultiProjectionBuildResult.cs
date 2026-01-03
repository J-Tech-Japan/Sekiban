namespace Sekiban.Dcb.MultiProjections;

public enum BuildStatus
{
    Success,
    Skipped,
    Failed
}

public sealed record ProjectorBuildResult(
    string ProjectorName,
    string ProjectorVersion,
    BuildStatus Status,
    string Reason,
    long EventsProcessed);

public sealed record MultiProjectionBuildResult(IReadOnlyList<ProjectorBuildResult> Results);
