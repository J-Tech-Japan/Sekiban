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
