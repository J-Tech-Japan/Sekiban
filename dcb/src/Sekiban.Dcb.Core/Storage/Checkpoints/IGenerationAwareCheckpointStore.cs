using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Storage.Checkpoints;

/// <summary>
///     SEK-G20 OPTIONAL checkpoint-store surface (feature-detected via <c>is IGenerationAwareCheckpointStore</c>, exactly
///     like the G15 <c>IConditionalEventStore</c> discipline). It adds NO member to <see cref="IMultiProjectionStateStore" />
///     and NO field to the frozen positional records — the generation / tombstone / exact-CAS-token control plane travels
///     through the versioned DTOs in this namespace.
///     <para>
///     The state machine is fixed: <c>Active(g,rev)</c> --Invalidate--&gt; <c>Tombstoned(g+1,rev')</c> --CommitRebuilt--&gt;
///     <c>Active(g+1,new rev)</c>. A first-ever row is created by an expected-absence CAS at <c>Active(0, rev0)</c>. Every
///     operation is conditional on the EXACT observed token; a generation-only comparison is not a CAS. The rebuilt commit
///     and the tombstone clear are ONE atomic same-row CAS. Exactly one writer wins each transition; concurrent stale
///     writers get <see cref="CheckpointCasStatus.ConditionRejected" /> (a refetch signal, never a fault).
///     </para>
///     A store that implements this interface MUST advertise <see cref="CheckpointCapabilityKind.GenerationTombstoneCas" />
///     via <see cref="ICheckpointStoreCapabilityProvider.DescribeCheckpointCapability" /> — not implementing the interface
///     means the capability is unsupported (no default pass-through), and the product path then fails closed to the G14
///     fault path on a retrograde invalidation rather than silently doing an unconditional legacy write.
/// </summary>
public interface IGenerationAwareCheckpointStore : ICheckpointStoreCapabilityProvider
{
    /// <summary>
    ///     Reads the control plane (generation, exact token, lifecycle) and, when Active, the payload record — atomically,
    ///     as one unit. On a capable store this MUST be consulted BEFORE any legacy payload read on activation, so a
    ///     tombstone forces a rebuild rather than binding a stale payload. Returns <see cref="CheckpointSlot.Absent" />
    ///     when no row exists.
    /// </summary>
    Task<ResultBox<CheckpointSlot>> ReadCheckpointSlotAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Normal product persist as an expected-token CAS. <paramref name="expectation" /> is either
    ///     <see cref="CheckpointExpectation.Absent" /> (first-ever create at generation 0) or the exact Active slot
    ///     observed by a prior read. On success the row advances to <c>Active(sameGeneration, newRevision)</c> with the new
    ///     payload. A stale expectation yields <see cref="CheckpointCasStatus.ConditionRejected" /> and does NOT mutate the
    ///     row/offload/payload.
    /// </summary>
    Task<CheckpointCasOutcome> ConditionalUpsertAsync(
        MultiProjectionStateWriteRequest payload,
        Stream stream,
        CheckpointExpectation expectation,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Durable bump+tombstone invalidation: <c>Active(g,rev)</c> --CAS--&gt; <c>Tombstoned(g+1,rev')</c>. Replaces the
    ///     delete-based invalidation. The expectation is the exact Active slot; the tombstone is durably visible to other
    ///     clusters BEFORE the local rebuild proceeds. The prior authoritative payload/offload identity is left intact
    ///     under the tombstone (readers gate on the tombstone, not on payload absence).
    /// </summary>
    Task<CheckpointCasOutcome> InvalidateWithTombstoneAsync(
        string projectorName,
        string projectorVersion,
        CheckpointExpectation expectation,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Atomic rebuilt-commit: <c>Tombstoned(g+1,rev')</c> --CAS--&gt; <c>Active(g+1,new rev)</c> writing the rebuilt
    ///     payload AND clearing the tombstone as ONE same-row CAS on the exact tombstone token. Exactly one rebuilder wins;
    ///     the losers get <see cref="CheckpointCasStatus.ConditionRejected" /> and refetch. The expectation is the exact
    ///     Tombstoned slot.
    /// </summary>
    Task<CheckpointCasOutcome> CommitRebuiltAsync(
        MultiProjectionStateWriteRequest payload,
        Stream stream,
        CheckpointExpectation expectation,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default);
}
