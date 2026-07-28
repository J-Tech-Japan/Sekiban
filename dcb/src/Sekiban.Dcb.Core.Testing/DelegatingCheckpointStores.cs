using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Storage.Checkpoints;
namespace Sekiban.Dcb.Testing;

/// <summary>
///     A transparent decorator over an inner <see cref="IMultiProjectionStateStore" /> that forwards every member. Tests
///     that need a store which differs in ONE respect (a lying capability descriptor, a parked write, a composite that
///     intersects capability) derive from this / <see cref="DelegatingCheckpointStore" /> and override only that member —
///     so the (otherwise identical) forwarding boilerplate lives in exactly one place instead of being copied per test.
/// </summary>
public abstract class DelegatingMultiProjectionStateStore : IMultiProjectionStateStore
{
    protected readonly IMultiProjectionStateStore Inner;
    protected DelegatingMultiProjectionStateStore(IMultiProjectionStateStore inner) => Inner = inner;

    public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(string p, string v, CancellationToken ct = default) => Inner.GetLatestForVersionAsync(p, v, ct);
    public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(string p, CancellationToken ct = default) => Inner.GetLatestAnyVersionAsync(p, ct);
    public Task<ResultBox<bool>> UpsertAsync(MultiProjectionStateRecord r, int o = 1_000_000, CancellationToken ct = default) => Inner.UpsertAsync(r, o, ct);
    public Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(CancellationToken ct = default) => Inner.ListAllAsync(ct);
    public Task<ResultBox<bool>> DeleteAsync(string p, string v, CancellationToken ct = default) => Inner.DeleteAsync(p, v, ct);
    public Task<ResultBox<int>> DeleteAllAsync(string? p = null, CancellationToken ct = default) => Inner.DeleteAllAsync(p, ct);
    public Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(MultiProjectionStateRecord r, CancellationToken ct = default) => Inner.OpenStateDataReadStreamAsync(r, ct);
    public Task<ResultBox<bool>> UpsertFromStreamAsync(MultiProjectionStateWriteRequest r, Stream s, int o, CancellationToken ct = default) => Inner.UpsertFromStreamAsync(r, s, o, ct);
}

/// <summary>
///     A <see cref="DelegatingMultiProjectionStateStore" /> that ALSO implements the SEK-G20 generation/tombstone CAS
///     surface, forwarding it to a capable inner store. The CAS methods and the capability descriptor are
///     <c>virtual</c> so a test can override exactly one (e.g. park a persist, or disclaim the capability) without
///     re-declaring the rest.
/// </summary>
public abstract class DelegatingCheckpointStore : DelegatingMultiProjectionStateStore,
    IGenerationAwareCheckpointStore, ICheckpointStoreCapabilityProvider
{
    private readonly IGenerationAwareCheckpointStore _cas;
    protected DelegatingCheckpointStore(IGenerationAwareCheckpointStore inner) : base((IMultiProjectionStateStore)inner) => _cas = inner;

    public virtual CheckpointStoreCapabilityDescriptor DescribeCheckpointCapability() =>
        CheckpointCapabilityResolver.Describe(_cas, "delegating");
    public virtual Task<ResultBox<CheckpointSlot>> ReadCheckpointSlotAsync(string p, string v, CancellationToken ct = default) => _cas.ReadCheckpointSlotAsync(p, v, ct);
    public virtual Task<CheckpointCasOutcome> ConditionalUpsertAsync(MultiProjectionStateWriteRequest r, Stream s, CheckpointExpectation e, int o, CancellationToken ct = default) => _cas.ConditionalUpsertAsync(r, s, e, o, ct);
    public virtual Task<CheckpointCasOutcome> InvalidateWithTombstoneAsync(string p, string v, CheckpointExpectation e, CancellationToken ct = default) => _cas.InvalidateWithTombstoneAsync(p, v, e, ct);
    public virtual Task<CheckpointCasOutcome> CommitRebuiltAsync(MultiProjectionStateWriteRequest r, Stream s, CheckpointExpectation e, int o, CancellationToken ct = default) => _cas.CommitRebuiltAsync(r, s, e, o, ct);
}

/// <summary>
///     A transparent gating decorator over ANY generation-aware checkpoint store (InMemory reference OR a real provider
///     such as Postgres). It can PARK the next ConditionalUpsert / CommitRebuilt — the exact calls the sole
///     <c>CheckpointMutationCoordinator</c> makes — so a two-cluster test can stage a deterministic parked-stale-writer /
///     open-tombstone window against real product code, then release and observe the CAS outcome. Store-agnostic: the same
///     harness drives the InMemory reference and an authoritative Postgres row.
/// </summary>
public sealed class GatingCheckpointStore : DelegatingCheckpointStore
{
    public GatingCheckpointStore(IGenerationAwareCheckpointStore inner) : base(inner) { }

    /// <summary>A one-shot parking gate: <see cref="Arrived" /> completes when a matching op reaches the store boundary;
    ///     the op then blocks on <see cref="Release" /> until the test lets it proceed.</summary>
    public sealed class Gate
    {
        public readonly TaskCompletionSource Arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Release;
    }

    public Gate? UpsertGate;
    public Gate? CommitRebuiltGate;

    private static async Task Park(Gate gate)
    {
        gate.Arrived.TrySetResult();
        if (gate.Release is not null)
        {
            await gate.Release.Task.ConfigureAwait(false);
        }
    }

    public override async Task<CheckpointCasOutcome> ConditionalUpsertAsync(
        MultiProjectionStateWriteRequest r, Stream s, CheckpointExpectation e, int o, CancellationToken ct = default)
    {
        var gate = UpsertGate;
        if (gate is not null) { UpsertGate = null; await Park(gate); }   // one-shot: only the first persist parks
        return await base.ConditionalUpsertAsync(r, s, e, o, ct).ConfigureAwait(false);
    }

    public override async Task<CheckpointCasOutcome> CommitRebuiltAsync(
        MultiProjectionStateWriteRequest r, Stream s, CheckpointExpectation e, int o, CancellationToken ct = default)
    {
        var gate = CommitRebuiltGate;
        if (gate is not null) { CommitRebuiltGate = null; await Park(gate); }
        return await base.CommitRebuiltAsync(r, s, e, o, ct).ConfigureAwait(false);
    }
}
