using System.Text.Json.Serialization;

namespace Sekiban.Dcb;

/// <summary>
///     A passive, best-effort projection status sample.  The sample is composed from a heartbeat registry and
///     event-store counts; reading it never activates a projection grain.
/// </summary>
public sealed record ProjectionStatusSnapshot(
    string ProjectorName,
    string ProjectorVersion,
    string ClusterId,
    string ActivationId,
    long Sequence,
    long AppliedEventCount,
    string? LastAppliedSortableUniqueId,
    string? LastTraversedSortableUniqueId,
    long TotalEventCount,
    long RemainingEventCount,
    DateTimeOffset SampledAtUtc,
    string Consistency,
    bool IsCaughtUp,
    bool HasConflict = false,
    IReadOnlyList<string>? ConflictingActivationIds = null)
{
    /// <summary>
    ///     The only consistency claim made by this surface.  Counts are samples and are not an atomic head/count view.
    /// </summary>
    public const string BestEffortConsistency = "bestEffort";

    [JsonIgnore]
    public IReadOnlyList<string> ConflictActivations =>
        ConflictingActivationIds ?? Array.Empty<string>();
}

/// <summary>
///     Internal registry row written by a projection activation.  Rows are keyed by service, projector, cluster, and
///     activation.  <see cref="Sequence"/> is fenced by the status store so an old activation cannot silently win.
/// </summary>
public sealed record ProjectionStatusHeartbeat(
    string ServiceId,
    string ProjectorName,
    string ProjectorVersion,
    string ClusterId,
    string ActivationId,
    long Sequence,
    long AppliedEventCount,
    string? LastAppliedSortableUniqueId,
    string? LastTraversedSortableUniqueId,
    DateTimeOffset RecordedAtUtc)
{
    public ProjectionStatusHeartbeat WithSequence(long sequence, DateTimeOffset recordedAtUtc) =>
        this with { Sequence = sequence, RecordedAtUtc = recordedAtUtc };
}

/// <summary>
///     Compatibility name for callers that describe a heartbeat as a row.
/// </summary>
public sealed record ProjectionStatusRow(
    string ServiceId,
    string ProjectorName,
    string ProjectorVersion,
    string ClusterId,
    string ActivationId,
    long Sequence,
    long AppliedEventCount,
    string? LastAppliedSortableUniqueId,
    string? LastTraversedSortableUniqueId,
    DateTimeOffset RecordedAtUtc)
{
    public ProjectionStatusHeartbeat ToHeartbeat() => new(
        ServiceId,
        ProjectorName,
        ProjectorVersion,
        ClusterId,
        ActivationId,
        Sequence,
        AppliedEventCount,
        LastAppliedSortableUniqueId,
        LastTraversedSortableUniqueId,
        RecordedAtUtc);

    public static ProjectionStatusRow FromHeartbeat(ProjectionStatusHeartbeat heartbeat) => new(
        heartbeat.ServiceId,
        heartbeat.ProjectorName,
        heartbeat.ProjectorVersion,
        heartbeat.ClusterId,
        heartbeat.ActivationId,
        heartbeat.Sequence,
        heartbeat.AppliedEventCount,
        heartbeat.LastAppliedSortableUniqueId,
        heartbeat.LastTraversedSortableUniqueId,
        heartbeat.RecordedAtUtc);
}

public enum ProjectionStatusWriteOutcome
{
    Committed = 0,
    Conflict = 1
}

/// <summary>
///     Result of a heartbeat CAS.  A conflict is a normal stale-writer result and must be surfaced to the reader or
///     caller; it is never converted into an unconditional last-write-wins update.
/// </summary>
public sealed record ProjectionStatusWriteResult(
    ProjectionStatusWriteOutcome Outcome,
    ProjectionStatusHeartbeat? Current,
    string? ConflictReason = null)
{
    public bool Committed => Outcome == ProjectionStatusWriteOutcome.Committed;
    public bool Conflict => Outcome == ProjectionStatusWriteOutcome.Conflict;

    public static ProjectionStatusWriteResult Success(ProjectionStatusHeartbeat row) =>
        new(ProjectionStatusWriteOutcome.Committed, row);

    public static ProjectionStatusWriteResult Rejected(ProjectionStatusHeartbeat? current, string reason) =>
        new(ProjectionStatusWriteOutcome.Conflict, current, reason);
}

/// <summary>Configuration for the passive projection status registry and read-side sampling.</summary>
public class ProjectionStatusOptions
{
    public const string DefaultClusterId = "default";

    /// <summary>Logical cluster/host identity.  It is part of the registry key and must be stable for a deployment.</summary>
    public string ClusterId { get; set; } = DefaultClusterId;

    /// <summary>Dedicated heartbeat period.  The production default is the contract's 30 seconds.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a row remains eligible for a fresh-activation conflict check.</summary>
    public TimeSpan FreshnessWindow { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Bounded parallelism for cursor-specific event-count samples.</summary>
    public int MaxConcurrentReads { get; set; } = 8;

    /// <summary>Allows a host to turn the heartbeat writer off while retaining the read surface.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>Descriptive alias used by hosts that call the feature a registry.</summary>
public class ProjectionStatusRegistryOptions : ProjectionStatusOptions
{
}

/// <summary>Request used by the versioned serialized status boundary.</summary>
public sealed record ProjectionStatusReadRequest(
    string? ServiceId = null,
    string? ProjectorName = null,
    string? ProjectorVersion = null);

/// <summary>Version-one envelope for the serialized passive status surface.</summary>
public sealed record SerializedProjectionStatusEnvelopeV1(
    int Version,
    string ServiceId,
    IReadOnlyList<ProjectionStatusSnapshot> Snapshots)
{
    public const int CurrentVersion = 1;

    public static SerializedProjectionStatusEnvelopeV1 Create(
        string serviceId,
        IReadOnlyList<ProjectionStatusSnapshot> snapshots) =>
        new(CurrentVersion, serviceId, snapshots);
}

/// <summary>Typed errors used by the serialized status boundary before any store read is attempted.</summary>
public sealed class UnsupportedSerializedProjectionStatusVersionException : Exception
{
    public UnsupportedSerializedProjectionStatusVersionException(int version)
        : base($"Serialized projection status version {version} is not supported.") => Version = version;

    public int Version { get; }
}

public sealed class SerializedProjectionStatusShapeException : Exception
{
    public SerializedProjectionStatusShapeException(string message)
        : base(message)
    {
    }
}
