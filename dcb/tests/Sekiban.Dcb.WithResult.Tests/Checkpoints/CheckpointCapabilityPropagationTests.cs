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

    [Fact]
    public void DeceptiveDescriptor_advertisesCasWithoutImplementingTheInterface_isNotReportedCapable()
    {
        // A store whose DESCRIPTOR lies (claims GenerationTombstoneCas) but does NOT implement the CAS interface. Because
        // resolution requires the LIVE interface too, the lie cannot force CAS adoption onto a store that has no CAS API —
        // the product falls back fail-closed rather than invoking methods that do not exist.
        var lying = new LyingDescriptorNoInterfaceStore();
        Assert.True(lying.DescribeCheckpointCapability().Supports(Gen));            // the descriptor advertises it...
        Assert.False(lying is IGenerationAwareCheckpointStore);                     // ...but the API is absent...
        Assert.False(CheckpointCapabilityResolver.SupportsGenerationCas(lying));    // ...so it is NOT reported capable.
    }

    [Fact]
    public void DeceptiveInterface_implementsCasButDescriptorDisclaimsIt_isNotReportedCapable()
    {
        // The mirror-image deception: a store that HAS the CAS interface but whose descriptor honestly (or dishonestly)
        // reports None. Resolution requires the descriptor to positively advertise the kind, so a store that disclaims the
        // capability is treated as non-capable — the two signals must AGREE positively, and disagreement fails closed.
        var disclaiming = new CasInterfaceButDisclaimingDescriptorStore();
        Assert.True(disclaiming is IGenerationAwareCheckpointStore);                     // the API is present...
        Assert.False(disclaiming.DescribeCheckpointCapability().Supports(Gen));          // ...but the descriptor disclaims...
        Assert.False(CheckpointCapabilityResolver.SupportsGenerationCas(disclaiming));   // ...so it is NOT reported capable.
    }

    // A composite that honestly intersects the capability of its underlying stores (the discipline any real composite
    // MUST follow). Reads/writes delegate to the primary (capable inner); capability is the intersection.
    private sealed class HonestCompositeStore : DelegatingCheckpointStore
    {
        private readonly IReadOnlyList<object> _underlying;
        public HonestCompositeStore(InMemoryMultiProjectionStateStore capableInner, NonCapableStore? nonCapableInner) : base(capableInner) =>
            _underlying = nonCapableInner is null ? new object[] { capableInner } : new object[] { capableInner, nonCapableInner };
        public override CheckpointStoreCapabilityDescriptor DescribeCheckpointCapability() =>
            CheckpointStoreCapabilityDescriptor.Intersect("Composite",
                _underlying.Select(u => CheckpointCapabilityResolver.Describe(u, "inner")).ToList());
    }

    // Deception A: DESCRIBES itself as CAS-capable but does NOT implement IGenerationAwareCheckpointStore. The lie must not
    // grant capability — the resolver requires the actual API on the live instance.
    private sealed class LyingDescriptorNoInterfaceStore : DelegatingMultiProjectionStateStore, ICheckpointStoreCapabilityProvider
    {
        public LyingDescriptorNoInterfaceStore() : base(new InMemoryMultiProjectionStateStore()) { }
        public CheckpointStoreCapabilityDescriptor DescribeCheckpointCapability() =>
            CheckpointStoreCapabilityDescriptor.Supporting("Lying", CheckpointCapabilityKind.GenerationTombstoneCas);
    }

    // Deception B: IMPLEMENTS the CAS interface but its descriptor disclaims the capability (None). Capability must not be
    // granted — the two signals must AGREE positively; disagreement fails closed.
    private sealed class CasInterfaceButDisclaimingDescriptorStore : DelegatingCheckpointStore
    {
        public CasInterfaceButDisclaimingDescriptorStore() : base(new InMemoryMultiProjectionStateStore()) { }
        public override CheckpointStoreCapabilityDescriptor DescribeCheckpointCapability() =>
            CheckpointStoreCapabilityDescriptor.None("Disclaiming");
    }

    // Deliberately does NOT implement IGenerationAwareCheckpointStore.
    private sealed class NonCapableStore : DelegatingMultiProjectionStateStore
    {
        public NonCapableStore() : base(new InMemoryMultiProjectionStateStore()) { }
    }
}
