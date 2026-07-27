namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Read-only view of <see cref="MultiProjectionGrainState" />. The coordinated state store hands callers this
///     interface for reads, so no caller can mutate the persisted payload outside a coordinated write.
/// </summary>
public interface IReadOnlyMultiProjectionGrainState
{
    string ProjectorName { get; }
    string? ProjectorVersion { get; }
    string? LastSortableUniqueId { get; }
    long EventsProcessed { get; }
    DateTime LastPersistTime { get; }
    string? SerializedState { get; }
    long StateSize { get; }
    string? SafeLastPosition { get; }
    string? LastPosition { get; }
    int LastGoodSafeVersion { get; }
    long LastGoodPayloadBytes { get; }
    long LastGoodOriginalSizeBytes { get; }
    long LastGoodEventsProcessed { get; }
    string? FaultEventId { get; }
    string? FaultEventType { get; }
    string? FaultPosition { get; }
    string? FaultMessage { get; }
    long FaultedAtUtcTicks { get; }
}

/// <summary>
///     SEK-G18: INTERNAL read seam for the durable "full ordered rebuild required" marker. Kept OFF the public
///     <see cref="IReadOnlyMultiProjectionGrainState" /> surface (packet: no public API additions) — the marker is an
///     Orleans-persisted implementation detail. A committed state read that satisfies <c>{ RebuildRequired: true }</c>
///     means a fresh activation must NOT restore the (possibly stale) external snapshot and must force a full ordered
///     replay from the authoritative store behind the shared query barrier. The marker is committed BEFORE the derived
///     external snapshot is invalidated, and cleared only after a rebuilt checkpoint is durably committed (old state
///     deserializes to false — no migration).
/// </summary>
internal interface IRebuildMarkerState
{
    bool RebuildRequired { get; }
}

/// <summary>
///     Lightweight persistent state for the multi-projection grain (v9).
///     Only stores key information for auxiliary/monitoring purposes.
///     Full state is stored in IMultiProjectionStateStore (Postgres/Cosmos).
/// </summary>
[GenerateSerializer]
public class MultiProjectionGrainState : IReadOnlyMultiProjectionGrainState
{
    [Id(0)]
    public string ProjectorName { get; set; } = string.Empty;

    [Id(1)]
    public string? ProjectorVersion { get; set; }

    [Id(2)]
    public string? LastSortableUniqueId { get; set; }

    [Id(3)]
    public long EventsProcessed { get; set; }

    [Id(4)]
    public DateTime LastPersistTime { get; set; }

    // Legacy fields for migration (will be ignored after migration)
    [Id(5)]
    public string? SerializedState { get; set; }

    [Id(6)]
    public long StateSize { get; set; }

    [Id(7)]
    public string? SafeLastPosition { get; set; }

    [Id(8)]
    public string? LastPosition { get; set; }

    // Integrity guard fields - tracks last known good state for detecting corruption
    [Id(9)]
    public int LastGoodSafeVersion { get; set; }

    [Id(10)]
    public long LastGoodPayloadBytes { get; set; }

    [Id(11)]
    public long LastGoodOriginalSizeBytes { get; set; }

    [Id(12)]
    public long LastGoodEventsProcessed { get; set; }

    // SEK-G14 persisted projection fault: a fold/deserialize failure the projection is stuck on. Restored on
    // activation so a freshly-activated grain cannot answer a query successfully before its known fault is re-established.
    [Id(13)]
    public string? FaultEventId { get; set; }

    [Id(14)]
    public string? FaultEventType { get; set; }

    [Id(15)]
    public string? FaultPosition { get; set; }

    [Id(16)]
    public string? FaultMessage { get; set; }

    [Id(17)]
    public long FaultedAtUtcTicks { get; set; }

    // SEK-G18: durable rebuild-required marker (see IRebuildMarkerState). INTERNAL — additive Orleans [Id(18)] serialized
    // by same-assembly codegen, kept off the public API surface; old state deserializes to false (no migration).
    [Id(18)]
    internal bool RebuildRequired { get; set; }

    // SEK-G20: the offending event identity behind the rebuild marker (additive internal [Id 19/20]; old state
    // deserializes to null). Carries FULL context (id + position) so a rebuild/fault names the poison, not just a boolean.
    [Id(19)]
    internal string? RebuildOffendingEventId { get; set; }

    [Id(20)]
    internal string? RebuildOffendingPosition { get; set; }

    /// <summary>
    ///     A complete copy of every field. All fields are value types or immutable strings, so a member-wise copy is a
    ///     deep clone. Used for copy-on-write: a write mutates a clone and publishes it only after a successful commit.
    /// </summary>
    public MultiProjectionGrainState Clone() =>
        new()
        {
            ProjectorName = ProjectorName,
            ProjectorVersion = ProjectorVersion,
            LastSortableUniqueId = LastSortableUniqueId,
            EventsProcessed = EventsProcessed,
            LastPersistTime = LastPersistTime,
            SerializedState = SerializedState,
            StateSize = StateSize,
            SafeLastPosition = SafeLastPosition,
            LastPosition = LastPosition,
            LastGoodSafeVersion = LastGoodSafeVersion,
            LastGoodPayloadBytes = LastGoodPayloadBytes,
            LastGoodOriginalSizeBytes = LastGoodOriginalSizeBytes,
            LastGoodEventsProcessed = LastGoodEventsProcessed,
            FaultEventId = FaultEventId,
            FaultEventType = FaultEventType,
            FaultPosition = FaultPosition,
            FaultMessage = FaultMessage,
            FaultedAtUtcTicks = FaultedAtUtcTicks,
            RebuildRequired = RebuildRequired,
            RebuildOffendingEventId = RebuildOffendingEventId,
            RebuildOffendingPosition = RebuildOffendingPosition
        };
}
