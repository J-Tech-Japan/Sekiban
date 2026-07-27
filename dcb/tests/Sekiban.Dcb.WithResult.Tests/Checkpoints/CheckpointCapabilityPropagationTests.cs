using System.Text;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.Tests.Checkpoints;

/// <summary>
///     SEK-G20 capability authority + composite propagation. Resolution is from the LIVE instance (never a type name),
///     and a composite reports <see cref="CheckpointCapabilityKind.GenerationTombstoneCas" /> ONLY when EVERY underlying
///     store supports it (intersection). Deceptive compositions — a wrapper that itself "implements" the interface but is
///     backed by a non-capable inner — must NOT be reported capable.
/// </summary>
public class CheckpointCapabilityPropagationTests
{
    private const CheckpointCapabilityKind Gen = CheckpointCapabilityKind.GenerationTombstoneCas;

    [Fact]
    public void Intersect_supportsAKind_onlyWhenEveryUnderlyingSupportsIt()
    {
        var capable = CheckpointStoreCapabilityDescriptor.Supporting("A", Gen);
        var none = CheckpointStoreCapabilityDescriptor.None("B");

        Assert.True(CheckpointStoreCapabilityDescriptor.Intersect("c", new[] { capable, capable }).Supports(Gen));
        Assert.False(CheckpointStoreCapabilityDescriptor.Intersect("c", new[] { capable, none }).Supports(Gen));
        Assert.False(CheckpointStoreCapabilityDescriptor.Intersect("c", Array.Empty<CheckpointStoreCapabilityDescriptor>()).Supports(Gen));
    }

    [Fact]
    public void Unknown_isNeverReportedAsACapability()
    {
        var d = CheckpointStoreCapabilityDescriptor.Supporting("A", CheckpointCapabilityKind.Unknown, Gen);
        Assert.False(d.Supports(CheckpointCapabilityKind.Unknown));
        Assert.True(d.Supports(Gen));
    }

    [Fact]
    public void Resolver_resolvesFromLiveInstance_notTypeName()
    {
        Assert.True(CheckpointCapabilityResolver.SupportsGenerationCas(new InMemoryMultiProjectionStateStore(new FixedServiceIdProvider("s"))));
        Assert.False(CheckpointCapabilityResolver.SupportsGenerationCas(new NonCapableStore()));
        Assert.False(CheckpointCapabilityResolver.SupportsGenerationCas(null));
    }

    [Fact]
    public void DeceptiveComposite_capableWrapperOverNonCapableInner_isNotReportedCapable()
    {
        // The composite implements the interface but honestly Intersects over its underlying stores — one of which is
        // non-capable — so it advertises None and the resolver reports it unsupported. A dishonest wrapper that lied
        // (advertised Gen while wrapping a non-capable inner) would be caught by the CAS itself failing, but the honest
        // Intersect discipline prevents the lie in the first place.
        var deceptive = new HonestCompositeStore(
            capableInner: new InMemoryMultiProjectionStateStore(new FixedServiceIdProvider("s")),
            nonCapableInner: new NonCapableStore());
        Assert.False(CheckpointCapabilityResolver.SupportsGenerationCas(deceptive));
        Assert.False(deceptive.DescribeCheckpointCapability().Supports(Gen));
    }

    [Fact]
    public void HonestComposite_overAllCapableInners_isReportedCapable()
    {
        var composite = new HonestCompositeStore(
            capableInner: new InMemoryMultiProjectionStateStore(new FixedServiceIdProvider("s")),
            nonCapableInner: null); // both capable
        Assert.True(CheckpointCapabilityResolver.SupportsGenerationCas(composite));
    }

    // A composite that honestly intersects the capability of its underlying stores (the discipline any real composite
    // MUST follow). Reads/writes delegate to the primary; capability is the intersection.
    private sealed class HonestCompositeStore : IMultiProjectionStateStore, IGenerationAwareCheckpointStore
    {
        private readonly InMemoryMultiProjectionStateStore _primary;
        private readonly IReadOnlyList<object> _underlying;

        public HonestCompositeStore(InMemoryMultiProjectionStateStore capableInner, NonCapableStore? nonCapableInner)
        {
            _primary = capableInner;
            _underlying = nonCapableInner is null ? new object[] { capableInner } : new object[] { capableInner, nonCapableInner };
        }

        public CheckpointStoreCapabilityDescriptor DescribeCheckpointCapability() =>
            CheckpointStoreCapabilityDescriptor.Intersect("Composite",
                _underlying.Select(u => CheckpointCapabilityResolver.Describe(u, "inner")).ToList());

        public Task<ResultBox<CheckpointSlot>> ReadCheckpointSlotAsync(string p, string v, CancellationToken ct = default) => _primary.ReadCheckpointSlotAsync(p, v, ct);
        public Task<CheckpointCasOutcome> ConditionalUpsertAsync(MultiProjectionStateWriteRequest r, Stream s, CheckpointExpectation e, int o, CancellationToken ct = default) => _primary.ConditionalUpsertAsync(r, s, e, o, ct);
        public Task<CheckpointCasOutcome> InvalidateWithTombstoneAsync(string p, string v, CheckpointExpectation e, CancellationToken ct = default) => _primary.InvalidateWithTombstoneAsync(p, v, e, ct);
        public Task<CheckpointCasOutcome> CommitRebuiltAsync(MultiProjectionStateWriteRequest r, Stream s, CheckpointExpectation e, int o, CancellationToken ct = default) => _primary.CommitRebuiltAsync(r, s, e, o, ct);

        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(string p, string v, CancellationToken ct = default) => _primary.GetLatestForVersionAsync(p, v, ct);
        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(string p, CancellationToken ct = default) => _primary.GetLatestAnyVersionAsync(p, ct);
        public Task<ResultBox<bool>> UpsertAsync(MultiProjectionStateRecord r, int o = 1_000_000, CancellationToken ct = default) => _primary.UpsertAsync(r, o, ct);
        public Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(CancellationToken ct = default) => _primary.ListAllAsync(ct);
        public Task<ResultBox<bool>> DeleteAsync(string p, string v, CancellationToken ct = default) => _primary.DeleteAsync(p, v, ct);
        public Task<ResultBox<int>> DeleteAllAsync(string? p = null, CancellationToken ct = default) => _primary.DeleteAllAsync(p, ct);
        public Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(MultiProjectionStateRecord r, CancellationToken ct = default) => _primary.OpenStateDataReadStreamAsync(r, ct);
        public Task<ResultBox<bool>> UpsertFromStreamAsync(MultiProjectionStateWriteRequest r, Stream s, int o, CancellationToken ct = default) => _primary.UpsertFromStreamAsync(r, s, o, ct);
    }

    // Deliberately does NOT implement IGenerationAwareCheckpointStore.
    private sealed class NonCapableStore : IMultiProjectionStateStore
    {
        private readonly InMemoryMultiProjectionStateStore _inner = new();
        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(string p, string v, CancellationToken ct = default) => _inner.GetLatestForVersionAsync(p, v, ct);
        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(string p, CancellationToken ct = default) => _inner.GetLatestAnyVersionAsync(p, ct);
        public Task<ResultBox<bool>> UpsertAsync(MultiProjectionStateRecord r, int o = 1_000_000, CancellationToken ct = default) => _inner.UpsertAsync(r, o, ct);
        public Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(CancellationToken ct = default) => _inner.ListAllAsync(ct);
        public Task<ResultBox<bool>> DeleteAsync(string p, string v, CancellationToken ct = default) => _inner.DeleteAsync(p, v, ct);
        public Task<ResultBox<int>> DeleteAllAsync(string? p = null, CancellationToken ct = default) => _inner.DeleteAllAsync(p, ct);
        public Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(MultiProjectionStateRecord r, CancellationToken ct = default) => _inner.OpenStateDataReadStreamAsync(r, ct);
        public Task<ResultBox<bool>> UpsertFromStreamAsync(MultiProjectionStateWriteRequest r, Stream s, int o, CancellationToken ct = default) => _inner.UpsertFromStreamAsync(r, s, o, ct);
    }
}
