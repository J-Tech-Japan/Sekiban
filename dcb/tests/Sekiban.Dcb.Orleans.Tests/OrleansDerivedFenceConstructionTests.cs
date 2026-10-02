extern alias WithoutResultFacade;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TagConsistencyFence;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;
using WithoutExecutor = WithoutResultFacade::Sekiban.Dcb.Orleans.OrleansDcbExecutor;

namespace Sekiban.Dcb.Orleans.Tests;

public sealed class OrleansDerivedFenceConstructionTests
{
    private static readonly DcbDomainTypes Domain = G20Shared.BuildDomain();
    private const string Service = DefaultServiceIdProvider.DefaultServiceId;
    public class UnusedCluster : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("Construction must not contact a cluster.");
    }
    private static IClusterClient Client() => DispatchProxy.Create<IClusterClient, UnusedCluster>();
    private sealed class NoUser : IExecutedUserProvider { public string GetExecutedUser() => "test"; }
    private sealed class NoPublisher : IEventPublisher
    {
        public Task PublishAsync(IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events, CancellationToken ct = default) => Task.CompletedTask;
    }
    private static object? Field(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
    private static object Core(object executor)
    {
        var construction = Field(executor, "_construction")!;
        var general = construction.GetType().GetProperty("GeneralExecutor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(construction)!;
        return Field(general, "_core")!;
    }
    private static readonly object LegacyDefaults = Core(new OrleansDcbExecutor(Client(), new RecordingStore(), Domain, null, null, null));

    private static void AssertWiring(object executor, IServiceIdProvider? sip = null, IEventPublisher? publisher = null,
        IExecutedUserProvider? user = null, ExecutorSizeGateOptions? gate = null,
        TagConsistencyFenceOptions? fence = null, ISortableUniqueIdGenerator? generator = null,
        SortableUniqueIdSeedCoordinator? seed = null)
    {
        var core = Core(executor);
        Assert.Equal(sip?.GetCurrentServiceId() ?? Service, ((IServiceIdProvider)Field(core, "_serviceIdProvider")!).GetCurrentServiceId());
        Assert.Same(publisher, Field(core, "_eventPublisher"));
        Assert.Same(user, Field(core, "_executedUserProvider"));
        Assert.Same(gate, Field(core, "_executorSizeGateOptions"));
        Assert.Equal(fence?.Mode ?? TagConsistencyFenceMode.Off,
            (Field(core, "_tagConsistencyFenceOptions") as TagConsistencyFenceOptions)?.Mode ?? TagConsistencyFenceMode.Off);
        Assert.Same(generator ?? Field(LegacyDefaults, "_sortableUniqueIdGenerator"), Field(core, "_sortableUniqueIdGenerator"));
        Assert.Same(seed ?? Field(LegacyDefaults, "_sortableUniqueIdSeedCoordinator"), Field(core, "_sortableUniqueIdSeedCoordinator"));
    }
    [Fact]
    public void OrleansDcbExecutor_CallShapesCompileAndPreserveDependencies()
    {
        var cc = Client(); var es = new RecordingStore(); var dt = Domain;
        var p = new NoPublisher(); var sip = new FixedServiceIdProvider("tenant-x"); var u = new NoUser();
        var gate = new ExecutorSizeGateOptions();
        var fence = new TagConsistencyFenceOptions { Mode = TagConsistencyFenceMode.DeriveFromReservations };
        var gen = new MonotonicSortableUniqueIdGenerator(); var seed = new SortableUniqueIdSeedCoordinator(gen);
        AssertWiring(new OrleansDcbExecutor(cc, es, dt));
        AssertWiring(new OrleansDcbExecutor(cc, es, dt, p), publisher: p);
        AssertWiring(new OrleansDcbExecutor(cc, es, dt, p, sip), sip, p);
        AssertWiring(new OrleansDcbExecutor(cc, es, dt, p, sip, u), sip, p, u);
        AssertWiring(new OrleansDcbExecutor(cc, es, dt, serviceIdProvider: sip), sip);
        AssertWiring(new OrleansDcbExecutor(cc, es, dt, eventPublisher: p, executedUserProvider: u), publisher: p, user: u);
        AssertWiring(new OrleansDcbExecutor(cc, es, dt, gate), gate: gate);
        AssertWiring(new OrleansDcbExecutor(cc, es, dt, gate, p), publisher: p, gate: gate);
        AssertWiring(new OrleansDcbExecutor(cc, es, dt, executorSizeGateOptions: gate), gate: gate);
        AssertWiring(new OrleansDcbExecutor(cc, es, dt, executorSizeGateOptions: gate, serviceIdProvider: sip), sip, gate: gate);
        AssertWiring(new OrleansDcbExecutor(cc, es, dt, p, sip, u, gen, seed), sip, p, u, generator: gen, seed: seed);
        AssertWiring(new OrleansDcbExecutor(cc, es, dt, tagConsistencyFenceOptions: fence), fence: fence);
        AssertWiring(new OrleansDcbExecutor(cc, es, dt, sizeGateOptions: gate, tagConsistencyFenceOptions: fence), gate: gate, fence: fence);
        AssertWiring(new OrleansDcbExecutor(cc, es, dt, sortableUniqueIdGenerator: gen));
        AssertWiring(new OrleansDcbExecutor(cc, es, dt, sortableUniqueIdSeedCoordinator: seed));
    }
    [Fact]
    public void WithoutExecutor_CallShapesCompileAndPreserveDependencies()
    {
        var cc = Client(); var es = new RecordingStore(); var dt = Domain;
        var p = new NoPublisher(); var sip = new FixedServiceIdProvider("tenant-x"); var u = new NoUser();
        var gate = new ExecutorSizeGateOptions();
        var fence = new TagConsistencyFenceOptions { Mode = TagConsistencyFenceMode.DeriveFromReservations };
        var gen = new MonotonicSortableUniqueIdGenerator(); var seed = new SortableUniqueIdSeedCoordinator(gen);
        AssertWiring(new WithoutExecutor(cc, es, dt));
        AssertWiring(new WithoutExecutor(cc, es, dt, p), publisher: p);
        AssertWiring(new WithoutExecutor(cc, es, dt, p, sip), sip, p);
        AssertWiring(new WithoutExecutor(cc, es, dt, p, sip, u), sip, p, u);
        AssertWiring(new WithoutExecutor(cc, es, dt, serviceIdProvider: sip), sip);
        AssertWiring(new WithoutExecutor(cc, es, dt, eventPublisher: p, executedUserProvider: u), publisher: p, user: u);
        AssertWiring(new WithoutExecutor(cc, es, dt, gate), gate: gate);
        AssertWiring(new WithoutExecutor(cc, es, dt, gate, p), publisher: p, gate: gate);
        AssertWiring(new WithoutExecutor(cc, es, dt, executorSizeGateOptions: gate), gate: gate);
        AssertWiring(new WithoutExecutor(cc, es, dt, executorSizeGateOptions: gate, serviceIdProvider: sip), sip, gate: gate);
        AssertWiring(new WithoutExecutor(cc, es, dt, p, sip, u, gen, seed), sip, p, u, generator: gen, seed: seed);
        AssertWiring(new WithoutExecutor(cc, es, dt, tagConsistencyFenceOptions: fence), fence: fence);
        AssertWiring(new WithoutExecutor(cc, es, dt, sizeGateOptions: gate, tagConsistencyFenceOptions: fence), gate: gate, fence: fence);
        AssertWiring(new WithoutExecutor(cc, es, dt, sortableUniqueIdGenerator: gen));
        AssertWiring(new WithoutExecutor(cc, es, dt, sortableUniqueIdSeedCoordinator: seed));
    }
    public static IEnumerable<object[]> DiCases()
    {
        foreach (var type in new[] { typeof(OrleansDcbExecutor), typeof(WithoutExecutor) })
        foreach (var gate in new[] { false, true })
        foreach (var fence in new[] { false, true })
        foreach (var allocator in new[] { false, true })
        foreach (var optional in new[] { false, true })
        foreach (var custom in new[] { false, true })
        foreach (var activator in new[] { false, true })
            yield return [type, gate, fence, allocator, optional, custom, activator];
    }
    [Theory]
    [MemberData(nameof(DiCases))]
    public void DiMatrix_HonorsEveryRegisteredDependency_AndFailsClosed(Type type, bool gate, bool fence,
        bool allocator, bool optional, bool custom, bool activator)
    {
        var cc = Client(); var store = new RecordingStore();
        var services = new ServiceCollection();
        services.AddSingleton(cc); services.AddSingleton<IEventStore>(store); services.AddSingleton(Domain);
        if (gate) services.AddSekibanDcbExecutorSizeGate(_ => { });
        if (fence) services.AddSekibanDcbTagConsistencyFence(o => o.Mode = TagConsistencyFenceMode.DeriveFromReservations);
        if (allocator) services.AddSekibanDcbSortableUniqueIdGenerator();
        if (optional) { services.AddSingleton<IEventPublisher, NoPublisher>(); services.AddSingleton<IExecutedUserProvider, NoUser>(); }
        if (custom) services.AddSingleton<IServiceIdProvider>(new FixedServiceIdProvider("tenant-x"));
        services.AddTransient(type);
        using var sp = services.BuildServiceProvider();
        object Resolve() => activator ? ActivatorUtilities.CreateInstance(sp, type, cc, store, Domain) : sp.GetRequiredService(type);
        AssertWiring(Resolve(), sp.GetService<IServiceIdProvider>(), sp.GetService<IEventPublisher>(),
            sp.GetService<IExecutedUserProvider>(), sp.GetService<ExecutorSizeGateOptions>(),
            sp.GetService<TagConsistencyFenceOptions>(), sp.GetService<ISortableUniqueIdGenerator>(),
            sp.GetService<SortableUniqueIdSeedCoordinator>());
        store.Supported = false;
        if (fence) Assert.Throws<InvalidOperationException>(() => Resolve());
        else Assert.NotNull(Resolve());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiPartialAllocatorPair_UsesLegacyProcessSharedPair(bool generatorOnly)
    {
        foreach (var type in new[] { typeof(OrleansDcbExecutor), typeof(WithoutExecutor) })
        {
            var services = new ServiceCollection();
            services.AddSingleton(Client()); services.AddSingleton<IEventStore>(new RecordingStore()); services.AddSingleton(Domain);
            var generator = new MonotonicSortableUniqueIdGenerator();
            if (generatorOnly) services.AddSingleton<ISortableUniqueIdGenerator>(generator);
            else services.AddSingleton(new SortableUniqueIdSeedCoordinator(generator));
            services.AddTransient(type);
            using var sp = services.BuildServiceProvider();
            AssertWiring(sp.GetRequiredService(type));
            AssertWiring(ActivatorUtilities.CreateInstance(sp, type));
        }
    }

    [Fact]
    public void DirectFenceConstruction_RejectsUnsupportedStoreForBothFacades()
    {
        var store = new RecordingStore { Supported = false };
        var fence = new TagConsistencyFenceOptions { Mode = TagConsistencyFenceMode.DeriveFromReservations };
        Assert.Throws<InvalidOperationException>(() => new OrleansDcbExecutor(Client(), store, Domain, tagConsistencyFenceOptions: fence));
        Assert.Throws<InvalidOperationException>(() => new WithoutExecutor(Client(), store, Domain, tagConsistencyFenceOptions: fence));
    }

    [Fact]
    public async Task GlobalFence_MissingEpochStopsBeforeEitherFacadeHandler()
    {
        var store = new RecordingStore { Enabled = false };
        var fence = new TagConsistencyFenceOptions { Mode = TagConsistencyFenceMode.DeriveFromReservations };
        var called = false;
        var withResult = new OrleansDcbExecutor(Client(), store, Domain, tagConsistencyFenceOptions: fence);
        var result = await withResult.ExecuteCommandAsync(_ =>
        {
            called = true;
            return Task.FromResult(EventOrNone.None);
        });
        Assert.IsType<TagHeadEnforcementNotEnabledException>(result.GetException());
        var without = new WithoutExecutor(Client(), store, Domain, tagConsistencyFenceOptions: fence);
        await Assert.ThrowsAsync<TagHeadEnforcementNotEnabledException>(() => without.ExecuteCommandAsync(_ =>
        {
            called = true;
            return Task.FromResult(EventOrNone.Empty);
        }));
        Assert.False(called);
        Assert.Equal(2, store.EpochChecks);
        Assert.Equal(0, store.LegacyWrites);
        Assert.Null(store.Specification);
    }

    private sealed class RecordingStore : IEventStore, IExpectedTagPositionEventStore, IWriteConditionCapabilityProvider
    {
        private readonly InMemoryEventStore _inner = new(Domain.EventTypes);
        public string? ExpectedTagPositionServiceId { get; init; }
        public bool Enabled = true, Supported = true;
        public int LegacyWrites, EpochChecks;
        public ExpectedTagPositionSpecification? Specification;
        public WriteConditionCapabilityDescriptor DescribeWriteConditions() => Supported
            ? WriteConditionCapabilityDescriptor.Supporting("Recording", WriteConditionKind.ExpectedTagPosition)
            : WriteConditionCapabilityDescriptor.None("Recording");
        public Task<ResultBox<bool>> EnsureExpectedTagPositionEnforcementEnabledAsync(CancellationToken ct = default)
        {
            EpochChecks++;
            return Task.FromResult(Enabled ? ResultBox.FromValue(Enabled) : ResultBox.Error<bool>(new TagHeadEnforcementNotEnabledException(Service)));
        }
        public async Task<ResultBox<ExpectedTagPositionWriteResult>> WriteSerializableEventsWithExpectedTagPositionsAsync(
            IReadOnlyList<SerializableEvent> events, ExpectedTagPositionSpecification specification, CancellationToken ct = default)
        {
            Specification = specification;
            var result = (await _inner.WriteSerializableEventsAsync(events)).GetValue();
            return ResultBox.FromValue(new ExpectedTagPositionWriteResult(result.Events, result.TagWrites));
        }
        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => _inner.ReadTagsAsync(tag);
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => _inner.GetLatestTagAsync(tag);
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => _inner.TagExistsAsync(tag);
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => _inner.GetEventCountAsync(since);
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? group = null) => _inner.GetAllTagsAsync(group);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) => _inner.ReadAllSerializableEventsAsync(since);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? count) => _inner.ReadAllSerializableEventsAsync(since, count);
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid id) => _inner.ReadSerializableEventAsync(id);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) => _inner.ReadSerializableEventsByTagAsync(tag, since);
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => _inner.GetLatestSortableUniqueIdAsync();
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events)
        { LegacyWrites++; return _inner.WriteSerializableEventsAsync(events); }
    }

}
