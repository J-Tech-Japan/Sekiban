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

    /// <summary>Lifecycle phase reported by the activation that produced this sample.</summary>
    public string Phase { get; init; } = ProjectionStatusPhases.Unknown;

    /// <summary>Optional lease expiry for the activation heartbeat.</summary>
    public DateTimeOffset? LeaseExpiresAtUtc { get; init; }

    /// <summary>Whether the activation reported a projection fault.</summary>
    public bool IsFaulted { get; init; }

    /// <summary>A secret-free fault summary, when <see cref="IsFaulted"/> is true.</summary>
    public string? FaultMessage { get; init; }

    /// <summary>Whether this row satisfied the freshness/lease predicate at sampling time.</summary>
    public bool IsFresh { get; init; }

    /// <summary>Optional active-pointer transition classification supplied by an MV publisher.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SwitchKind { get; init; }

    /// <summary>Durable operator reason for a forced reverse. Omitted for ordinary projection heartbeats.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SwitchReason { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? SwitchedAtUtc { get; init; }

    /// <summary>Compatibility/readability alias for callers that call the lease boundary a lease.</summary>
    [JsonIgnore]
    public DateTimeOffset? LeaseUntilUtc => LeaseExpiresAtUtc;

    [JsonIgnore]
    public IReadOnlyList<string> ConflictActivations =>
        ConflictingActivationIds ?? Array.Empty<string>();

    /// <summary>
    ///     The projector version the caller expected to observe. It is intentionally excluded from V1 serialization;
    ///     the V2 envelope carries it together with <see cref="VersionDisposition"/>.
    /// </summary>
    [JsonIgnore]
    public string? ExpectedProjectorVersion { get; init; }

    /// <summary>The version physically observed on this row. <see cref="ProjectorVersion"/> remains the V1 name.</summary>
    [JsonIgnore]
    public string ObservedProjectorVersion => ProjectorVersion;

    /// <summary>Whether this row is current, a fresh different-version row, or stale/orphaned.</summary>
    [JsonIgnore]
    public ProjectionStatusVersionDisposition VersionDisposition { get; init; } = ProjectionStatusVersionDisposition.Current;

    /// <summary>True when the row is no longer fresh and cannot be presented as the sole current row.</summary>
    [JsonIgnore]
    public bool IsStaleOrOrphan => VersionDisposition == ProjectionStatusVersionDisposition.StaleOrOrphan;
}

/// <summary>
///     Internal registry row written by a projection activation.  The physical row is keyed by service, projector,
///     version, and cluster; <see cref="ActivationId"/> is row data so a replacement activation contends on the same
///     CAS row. <see cref="Sequence"/> is fenced by the status store so an old activation cannot silently win.
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
    /// <summary>Lifecycle phase reported by the activation.</summary>
    public string Phase { get; init; } = ProjectionStatusPhases.Unknown;

    /// <summary>Optional expiry of the activation's passive heartbeat lease.</summary>
    public DateTimeOffset? LeaseExpiresAtUtc { get; init; }

    /// <summary>Whether the activation is currently faulted.</summary>
    public bool IsFaulted { get; init; }

    /// <summary>A secret-free fault summary, when faulted.</summary>
    public string? FaultMessage { get; init; }

    public string? SwitchKind { get; init; }
    public string? SwitchReason { get; init; }
    public DateTimeOffset? SwitchedAtUtc { get; init; }

    /// <summary>Compatibility/readability alias for callers that call the lease boundary a lease.</summary>
    [JsonIgnore]
    public DateTimeOffset? LeaseUntilUtc => LeaseExpiresAtUtc;

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
    public string Phase { get; init; } = ProjectionStatusPhases.Unknown;
    public DateTimeOffset? LeaseExpiresAtUtc { get; init; }
    public bool IsFaulted { get; init; }
    public string? FaultMessage { get; init; }
    public string? SwitchKind { get; init; }
    public string? SwitchReason { get; init; }
    public DateTimeOffset? SwitchedAtUtc { get; init; }

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
        RecordedAtUtc)
    {
        Phase = Phase,
        LeaseExpiresAtUtc = LeaseExpiresAtUtc,
        IsFaulted = IsFaulted,
        FaultMessage = FaultMessage,
        SwitchKind = SwitchKind,
        SwitchReason = SwitchReason,
        SwitchedAtUtc = SwitchedAtUtc
    };

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
        heartbeat.RecordedAtUtc)
    {
        Phase = heartbeat.Phase,
        LeaseExpiresAtUtc = heartbeat.LeaseExpiresAtUtc,
        IsFaulted = heartbeat.IsFaulted,
        FaultMessage = heartbeat.FaultMessage,
        SwitchKind = heartbeat.SwitchKind,
        SwitchReason = heartbeat.SwitchReason,
        SwitchedAtUtc = heartbeat.SwitchedAtUtc
    };
}

/// <summary>Stable wire values for passive projection lifecycle reporting.</summary>
public static class ProjectionStatusPhases
{
    public const string Unknown = "unknown";
    public const string Starting = "starting";
    public const string CatchingUp = "catchingUp";
    public const string Active = "active";
    public const string CaughtUp = "caughtUp";
    public const string Faulted = "faulted";
    public const string Stopped = "stopped";
}

public enum ProjectionStatusWriteOutcome
{
    Committed = 0,
    Conflict = 1
}

/// <summary>Provider-neutral classification for a rejected projection-status compare-and-set write.</summary>
public enum ProjectionStatusConflictReason
{
    RowAbsent = 0,
    RowAlreadyExists = 1,
    SequenceMismatch = 2,
    ProviderPreconditionFailed = 3
}

/// <summary>
///     Typed, secret-free diagnostics for a rejected projection-status write. The physical row identity is the
///     service/projector/version/cluster tuple; ActivationId remains row data and is included only as observed data.
/// </summary>
public sealed record ProjectionStatusWriteConflict(
    ProjectionStatusConflictReason Reason,
    long ExpectedSequence,
    long? ObservedSequence,
    string ExpectedProjectorVersion,
    string? ObservedProjectorVersion,
    string ExpectedActivationId,
    string? ObservedActivationId,
    string? ProviderCondition = null)
{
    /// <summary>Compatibility text derived solely from the typed payload.</summary>
    public string ToCompatibilityReason() => Reason switch
    {
        ProjectionStatusConflictReason.RowAbsent =>
            $"Heartbeat CAS rejected: expected sequence {ExpectedSequence}, row is absent.",
        ProjectionStatusConflictReason.RowAlreadyExists =>
            $"Heartbeat CAS rejected: expected sequence {ExpectedSequence}, row already exists.",
        ProjectionStatusConflictReason.SequenceMismatch => ObservedSequence.HasValue
            ? $"Heartbeat CAS rejected: expected sequence {ExpectedSequence}, current sequence {ObservedSequence.Value}."
            : $"Heartbeat CAS rejected: expected sequence {ExpectedSequence}, current sequence is unavailable.",
        ProjectionStatusConflictReason.ProviderPreconditionFailed =>
            "Heartbeat CAS rejected by provider precondition.",
        _ => "Heartbeat CAS rejected."
    };
}

/// <summary>
///     The physical heartbeat identity pinned for one projection activation. It is captured from registered projector
///     metadata and must never be rebuilt from mutable persisted projection state.
/// </summary>
public sealed record ProjectionStatusWriterIdentity(
    string ServiceId,
    string ProjectorName,
    string ProjectorVersion,
    string ClusterId);

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

    /// <summary>
    ///     Structured diagnostics for a rejected write. The legacy <see cref="ConflictReason"/> text remains for
    ///     source and binary compatibility and is derived from this payload for new provider paths.
    /// </summary>
    public ProjectionStatusWriteConflict? ConflictDetails { get; init; }

    /// <summary>Alias for callers that describe <see cref="ConflictDetails"/> as a payload.</summary>
    [JsonIgnore]
    public ProjectionStatusWriteConflict? ConflictPayload => ConflictDetails;

    public static ProjectionStatusWriteResult Success(ProjectionStatusHeartbeat row) =>
        new(ProjectionStatusWriteOutcome.Committed, row);

    public static ProjectionStatusWriteResult Rejected(ProjectionStatusHeartbeat? current, string reason) =>
        new(ProjectionStatusWriteOutcome.Conflict, current, reason);

    /// <summary>
    ///     Creates a provider-neutral rejection while retaining the established three-argument record constructor and
    ///     string compatibility surface. Providers may force <paramref name="reason"/> when a native precondition
    ///     failure is observed after a reread.
    /// </summary>
    public static ProjectionStatusWriteResult Rejected(
        ProjectionStatusHeartbeat expected,
        long expectedSequence,
        ProjectionStatusHeartbeat? current,
        ProjectionStatusConflictReason? reason = null,
        string? providerCondition = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var resolvedReason = reason ?? Classify(expected, expectedSequence, current);
        var details = new ProjectionStatusWriteConflict(
            resolvedReason,
            expectedSequence,
            current?.Sequence,
            expected.ProjectorVersion,
            current?.ProjectorVersion,
            expected.ActivationId,
            current?.ActivationId,
            providerCondition);
        return new ProjectionStatusWriteResult(
            ProjectionStatusWriteOutcome.Conflict,
            current,
            details.ToCompatibilityReason())
        {
            ConflictDetails = details
        };
    }

    private static ProjectionStatusConflictReason Classify(
        ProjectionStatusHeartbeat expected,
        long expectedSequence,
        ProjectionStatusHeartbeat? current)
    {
        if (current is null)
        {
            return expectedSequence > 0
                ? ProjectionStatusConflictReason.RowAbsent
                : ProjectionStatusConflictReason.ProviderPreconditionFailed;
        }

        if (expectedSequence == 0)
        {
            return ProjectionStatusConflictReason.RowAlreadyExists;
        }

        return current.Sequence != expectedSequence || expected.Sequence <= current.Sequence
            ? ProjectionStatusConflictReason.SequenceMismatch
            : ProjectionStatusConflictReason.ProviderPreconditionFailed;
    }
}

/// <summary>How a passive reader classified a row relative to its expected version and freshness boundary.</summary>
public enum ProjectionStatusVersionDisposition
{
    Current = 0,
    VersionMismatch = 1,
    StaleOrOrphan = 2
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

    /// <summary>
    ///     Denominator sampling window.  A reader reuses one event-store head-count sample per service during this
    ///     window; cursor-specific numerator samples remain per distinct cursor in each read.
    /// </summary>
    public TimeSpan SamplingWindow { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum time allowed for one best-effort heartbeat upsert.</summary>
    public TimeSpan HeartbeatWriteTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Minimum interval between repeated heartbeat failure/conflict log entries.</summary>
    public TimeSpan HeartbeatFailureLogInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Initial delay for a failed heartbeat retry.</summary>
    public TimeSpan HeartbeatRetryBase { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum delay for exponential heartbeat retries.</summary>
    public TimeSpan HeartbeatRetryCap { get; set; } = TimeSpan.FromSeconds(30);

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
    string? ProjectorVersion = null)
{
    /// <summary>
    ///     Expected active version used for classification, not filtering. When supplied, the reader keeps rows from
    ///     other versions visible so rolling deployments and expired orphans can be diagnosed rather than rejected.
    /// </summary>
    [JsonIgnore]
    public string? ExpectedProjectorVersion { get; init; }
}

/// <summary>Version-one request envelope for the serialized passive status boundary.</summary>
public sealed record SerializedProjectionStatusRequestEnvelopeV1(
    int Version,
    string? ServiceId,
    string? ProjectorName,
    string? ProjectorVersion)
{
    public const int CurrentVersion = 1;

    public static SerializedProjectionStatusRequestEnvelopeV1 Create(ProjectionStatusReadRequest? request) =>
        new(
            CurrentVersion,
            request?.ServiceId,
            request?.ProjectorName,
            request?.ProjectorVersion);

    public ProjectionStatusReadRequest ToRequest() =>
        new(ServiceId, ProjectorName, ProjectorVersion);
}

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

/// <summary>Version-two request envelope that adds an expected-version observation without changing V1 vectors.</summary>
public sealed record SerializedProjectionStatusRequestEnvelopeV2(
    int Version,
    string? ServiceId,
    string? ProjectorName,
    string? ProjectorVersion,
    string? ExpectedProjectorVersion)
{
    public const int CurrentVersion = 2;

    public static SerializedProjectionStatusRequestEnvelopeV2 Create(ProjectionStatusReadRequest? request) =>
        new(
            CurrentVersion,
            request?.ServiceId,
            request?.ProjectorName,
            request?.ProjectorVersion,
            request?.ExpectedProjectorVersion);

    public ProjectionStatusReadRequest ToRequest() =>
        new(ServiceId, ProjectorName, ProjectorVersion)
        {
            ExpectedProjectorVersion = ExpectedProjectorVersion
        };
}

/// <summary>V2 wrapper for a V1-shaped snapshot plus explicit version-observation diagnostics.</summary>
public sealed record SerializedProjectionStatusSnapshotV2(
    ProjectionStatusSnapshot Snapshot,
    string? ExpectedProjectorVersion,
    string ObservedProjectorVersion,
    ProjectionStatusVersionDisposition VersionDisposition,
    bool IsStaleOrOrphan)
{
    public static SerializedProjectionStatusSnapshotV2 Create(ProjectionStatusSnapshot snapshot) =>
        new(
            snapshot,
            snapshot.ExpectedProjectorVersion,
            snapshot.ObservedProjectorVersion,
            snapshot.VersionDisposition,
            snapshot.IsStaleOrOrphan);
}

/// <summary>Version-two serialized passive status envelope with version-disposition diagnostics.</summary>
public sealed record SerializedProjectionStatusEnvelopeV2(
    int Version,
    string ServiceId,
    IReadOnlyList<SerializedProjectionStatusSnapshotV2> Snapshots)
{
    public const int CurrentVersion = 2;

    public static SerializedProjectionStatusEnvelopeV2 Create(
        string serviceId,
        IReadOnlyList<ProjectionStatusSnapshot> snapshots) =>
        new(CurrentVersion, serviceId, snapshots.Select(SerializedProjectionStatusSnapshotV2.Create).ToArray());
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
