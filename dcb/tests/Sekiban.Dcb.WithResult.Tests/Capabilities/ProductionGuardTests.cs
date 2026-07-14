using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.WithResult.Tests.Capabilities;

/// <summary>
///     The matrix that matters. A downstream production system registered an in-memory executor as its
///     <c>ISekibanExecutor</c>; its one-argument constructor created a private volatile store; every command succeeded;
///     no event ever reached Cosmos; and nothing at startup could tell. These tests are the "could tell".
///     Every case builds a real host and really starts it, because the guard's entire job is to stop a host from
///     starting — asserting on a method in isolation would prove something weaker than the claim.
/// </summary>
public class ProductionGuardTests
{
    private static IHost BuildHost(
        string environment,
        object? executor,
        IEventStore? eventStore,
        IMultiProjectionStateStore? projectionStore,
        Action<SekibanDcbProductionGuardOptions>? configure = null,
        bool guard = true) =>
        new HostBuilder()
            .UseEnvironment(environment)
            .ConfigureServices(services =>
            {
                if (executor is not null)
                {
                    services.AddSingleton(typeof(ISekibanExecutor), executor);
                }

                if (eventStore is not null)
                {
                    services.AddSingleton(eventStore);
                }

                if (projectionStore is not null)
                {
                    services.AddSingleton(projectionStore);
                }

                if (guard)
                {
                    services.AddSekibanDcbProductionGuard(configure);
                }
                else
                {
                    services.AddSekibanDcbStartupBanner(configure);
                }
            })
            .Build();

    private static async Task<SekibanDcbProductionGuardException> AssertRefusesToStart(IHost host)
    {
        var thrown = await Assert.ThrowsAsync<SekibanDcbProductionGuardException>(() => host.StartAsync());
        await host.StopAsync();
        return thrown;
    }

    private static async Task AssertStarts(IHost host)
    {
        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task Production_DistributedExecutor_DurableStores_Starts()
    {
        using var host = BuildHost(
            Environments.Production,
            new FakeExecutor(ExecutorRuntimeKind.DistributedRuntime, "Orleans"),
            new FakeEventStore(StorageDurability.Durable, "Postgres"),
            new FakeProjectionStore(StorageDurability.Durable, "Postgres"));

        await AssertStarts(host);
    }

    [Fact]
    public async Task Production_DistributedExecutor_VolatileStore_RefusesToStart()
    {
        using var host = BuildHost(
            Environments.Production,
            new FakeExecutor(ExecutorRuntimeKind.DistributedRuntime, "Orleans"),
            new InMemoryEventStore(),
            new InMemoryMultiProjectionStateStore());

        var thrown = await AssertRefusesToStart(host);

        Assert.Contains("storage is volatile", thrown.Message);
        Assert.Equal(StorageDurability.Volatile, thrown.Report.EventStore.Durability);
    }

    [Fact]
    public async Task Production_DistributedExecutor_VolatileStore_WithStorageOverride_Starts()
    {
        using var host = BuildHost(
            Environments.Production,
            new FakeExecutor(ExecutorRuntimeKind.DistributedRuntime, "Orleans"),
            new InMemoryEventStore(),
            new InMemoryMultiProjectionStateStore(),
            options => options.AllowVolatileStorageInProduction = true);

        // An operator may decide their storage is disposable. They may not decide their executor is a test double.
        await AssertStarts(host);
    }

    [Fact]
    public async Task Production_TestingExecutor_DurableStores_RefusesToStart()
    {
        // Durable storage does not redeem a testing executor: in-process actors mean no cluster coordination, whatever
        // the events are being written to.
        using var host = BuildHost(
            Environments.Production,
            new FakeExecutor(ExecutorRuntimeKind.TestingInProcess, "InMemory (in-process actors)"),
            new FakeEventStore(StorageDurability.Durable, "Postgres"),
            new FakeProjectionStore(StorageDurability.Durable, "Postgres"));

        var thrown = await AssertRefusesToStart(host);

        Assert.Contains("Production requires a distributed runtime", thrown.Message);
    }

    [Fact]
    public async Task Production_TestingExecutor_VolatileStore_RefusesToStart()
    {
        // The incident, reproduced: the executor and the store are both test-shaped, and the host must not come up.
        using var host = BuildHost(
            Environments.Production,
            new FakeExecutor(ExecutorRuntimeKind.TestingInProcess, "InMemory (in-process actors)"),
            new InMemoryEventStore(),
            new InMemoryMultiProjectionStateStore());

        var thrown = await AssertRefusesToStart(host);

        Assert.Contains("Production requires a distributed runtime", thrown.Message);
        Assert.Contains("storage is volatile", thrown.Message);
    }

    /// <summary>
    ///     The override-separation proof the whole design turns on. Storage is authorised; the executor is not; the
    ///     host still does not start. If this ever passes, the one override we ship has quietly become the override we
    ///     refused to ship.
    /// </summary>
    [Fact]
    public async Task Production_TestingExecutor_StorageOverride_StillRefusesToStart()
    {
        using var host = BuildHost(
            Environments.Production,
            new FakeExecutor(ExecutorRuntimeKind.TestingInProcess, "InMemory (in-process actors)"),
            new InMemoryEventStore(),
            new InMemoryMultiProjectionStateStore(),
            options => options.AllowVolatileStorageInProduction = true);

        var thrown = await AssertRefusesToStart(host);

        Assert.Contains("Production requires a distributed runtime", thrown.Message);
        Assert.Contains("There is no override for this", thrown.Message);

        // The storage complaint is gone — the override did exactly, and only, what it says.
        Assert.DoesNotContain("storage is volatile", thrown.Message);
    }

    /// <summary>
    ///     The gap the review found, and it was a real one: Volatile and Unknown were one condition, so the
    ///     volatile-only override silently authorised an unidentified store too. They are different claims. "Volatile"
    ///     is a store telling you it loses data, and an operator can look at that and say they meant it. "Unknown" is a
    ///     store telling you nothing — there is nothing there for an operator to have meant.
    /// </summary>
    [Fact]
    public async Task Production_UnknownStorage_WithVolatileOverride_StillRefusesToStart()
    {
        using var host = BuildHost(
            Environments.Production,
            new FakeExecutor(ExecutorRuntimeKind.DistributedRuntime, "Orleans"),
            new FakeEventStore(null, "third-party"),
            new FakeProjectionStore(StorageDurability.Durable, "Postgres"),
            options => options.AllowVolatileStorageInProduction = true);

        var thrown = await AssertRefusesToStart(host);

        Assert.Contains("would not identify itself", thrown.Message);
        Assert.Contains("does not authorise this", thrown.Message);
        Assert.Equal(StorageDurability.Unknown, thrown.Report.EventStore.Durability);
    }

    [Fact]
    public async Task Production_VolatileStorage_WithVolatileOverride_Starts_ButOnlyBecauseTheStoreSaidVolatile()
    {
        // The pair to the test above: same override, same environment, and the only difference is that this store
        // answered the question. That difference is the whole point of the override.
        using var host = BuildHost(
            Environments.Production,
            new FakeExecutor(ExecutorRuntimeKind.DistributedRuntime, "Orleans"),
            new FakeEventStore(StorageDurability.Volatile, "InMemory"),
            new FakeProjectionStore(StorageDurability.Volatile, "InMemory"),
            options => options.AllowVolatileStorageInProduction = true);

        await AssertStarts(host);
    }

    /// <summary>
    ///     Sekiban's own Cosmos registrations are scoped. A hosted service holds the ROOT provider, and asking the root
    ///     provider for a scoped service throws under ValidateScopes — so before this fix, opting into the guard broke
    ///     a supported composition outright, in Development too, which is the opposite of "zero behaviour change".
    ///     ValidateScopes is on here precisely so this test fails if the guard ever resolves from the root again.
    /// </summary>
    [Fact]
    public async Task AScopedComposition_IsResolvedInsideAScope_NotFromTheRootProvider()
    {
        using var host = new HostBuilder()
            .UseEnvironment(Environments.Production)
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            })
            .ConfigureServices(services =>
            {
                services.AddScoped<ISekibanExecutor>(
                    _ => new FakeExecutor(ExecutorRuntimeKind.DistributedRuntime, "Orleans"));
                services.AddScoped<IEventStore>(_ => new FakeEventStore(StorageDurability.Durable, "CosmosDb"));
                services.AddScoped<IMultiProjectionStateStore>(
                    _ => new FakeProjectionStore(StorageDurability.Durable, "CosmosDb"));
                services.AddSekibanDcbProductionGuard();
            })
            .Build();

        // The failure this guards against is not a refusal — it is an InvalidOperationException from the container
        // before the guard can report anything at all.
        await AssertStarts(host);
    }

    [Fact]
    public async Task AScopedVolatileComposition_IsStillCaught()
    {
        // ...and resolving in a scope must not become a way to smuggle a volatile store past the guard.
        using var host = new HostBuilder()
            .UseEnvironment(Environments.Production)
            .UseDefaultServiceProvider(options => options.ValidateScopes = true)
            .ConfigureServices(services =>
            {
                services.AddScoped<ISekibanExecutor>(
                    _ => new FakeExecutor(ExecutorRuntimeKind.DistributedRuntime, "Orleans"));
                services.AddScoped<IEventStore>(_ => new InMemoryEventStore());
                services.AddScoped<IMultiProjectionStateStore>(_ => new InMemoryMultiProjectionStateStore());
                services.AddSekibanDcbProductionGuard();
            })
            .Build();

        var thrown = await AssertRefusesToStart(host);

        Assert.Equal(StorageDurability.Volatile, thrown.Report.EventStore.Durability);
    }

    [Fact]
    public async Task Production_UnknownExecutorAndUnknownStore_RefusesToStart()
    {
        // Silence is not a promise. A store and an executor that decline to describe themselves fail closed.
        using var host = BuildHost(
            Environments.Production,
            new SilentExecutor(),
            new FakeEventStore(null, "third-party"),
            new FakeProjectionStore(null, "third-party"));

        var thrown = await AssertRefusesToStart(host);

        Assert.Equal(ExecutorRuntimeKind.Unknown, thrown.Report.Executor.Runtime);
        Assert.Equal(StorageDurability.Unknown, thrown.Report.EventStore.Durability);
    }

    [Fact]
    public async Task Production_NothingRegisteredAtAll_RefusesToStart()
    {
        using var host = BuildHost(Environments.Production, null, null, null);

        var thrown = await AssertRefusesToStart(host);

        Assert.Contains("(no ISekibanExecutor registered)", thrown.Report.Executor.RuntimeName);
    }

    [Fact]
    public async Task Development_TestingExecutorAndVolatileStore_StartsUnchanged()
    {
        // The everyday local setup. The guard warns in the banner and gets out of the way.
        using var host = BuildHost(
            Environments.Development,
            new FakeExecutor(ExecutorRuntimeKind.TestingInProcess, "InMemory (in-process actors)"),
            new InMemoryEventStore(),
            new InMemoryMultiProjectionStateStore());

        await AssertStarts(host);
    }

    [Fact]
    public async Task BannerOnly_InProduction_NeverFailsTheHost()
    {
        // AddSekibanDcbStartupBanner reports; it does not enforce. Opting into the guard is a separate act.
        using var host = BuildHost(
            Environments.Production,
            new FakeExecutor(ExecutorRuntimeKind.TestingInProcess, "InMemory"),
            new InMemoryEventStore(),
            new InMemoryMultiProjectionStateStore(),
            guard: false);

        await AssertStarts(host);
    }

    [Fact]
    public async Task AnEnvironmentTheOperatorCallsProduction_IsGuardedToo()
    {
        // Plenty of real production runs under a name ASP.NET Core does not consider Production.
        using var host = BuildHost(
            "prod-eu",
            new FakeExecutor(ExecutorRuntimeKind.TestingInProcess, "InMemory"),
            new FakeEventStore(StorageDurability.Durable, "Postgres"),
            new FakeProjectionStore(StorageDurability.Durable, "Postgres"),
            options => options.ProductionEnvironmentNames.Add("prod-eu"));

        await AssertRefusesToStart(host);
    }

    [Fact]
    public void NoOverrideExistsForATestingExecutor()
    {
        // Not "off by default" — absent. Anything named like an executor escape hatch is a bug in this PR.
        var escapeHatches = typeof(SekibanDcbProductionGuardOptions)
            .GetProperties()
            .Select(p => p.Name)
            .Where(name => name.Contains("Executor", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Testing", StringComparison.OrdinalIgnoreCase)
                || name.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(escapeHatches);
    }

    [Fact]
    public async Task TheGuardEvaluatesWhatWasRESOLVED_NotWhatWasRegisteredFirst()
    {
        // The failure mode in the field: a perfectly good registration, then something later replaces it. Reading the
        // IServiceCollection would see Postgres here. Resolving sees what the container will actually hand out.
        using var host = new HostBuilder()
            .UseEnvironment(Environments.Production)
            .ConfigureServices(services =>
            {
                services.AddSingleton<ISekibanExecutor>(
                    new FakeExecutor(ExecutorRuntimeKind.DistributedRuntime, "Orleans"));
                services.AddSingleton<IEventStore>(new FakeEventStore(StorageDurability.Durable, "Postgres"));
                services.AddSingleton<IMultiProjectionStateStore>(
                    new FakeProjectionStore(StorageDurability.Durable, "Postgres"));

                services.AddSekibanDcbProductionGuard();

                // ...and now someone "just for a moment" swaps the store. Last registration wins in DI.
                services.AddSingleton<IEventStore>(new InMemoryEventStore());
            })
            .Build();

        var thrown = await AssertRefusesToStart(host);

        Assert.Equal(StorageDurability.Volatile, thrown.Report.EventStore.Durability);
    }

    private sealed class FakeExecutor(ExecutorRuntimeKind runtime, string name)
        : StubExecutor, IExecutorRuntimeDescriptorProvider
    {
        public ExecutorRuntimeDescriptor DescribeRuntime() => new(runtime, name);
    }

    /// <summary>An executor that declines to describe itself — every third-party executor, until it opts in.</summary>
    private sealed class SilentExecutor : StubExecutor;

    private sealed class FakeEventStore(StorageDurability? durability, string provider)
        : SilentEventStore, IStorageDurabilityDescriptorProvider
    {
        public StorageDurabilityDescriptor DescribeStorage() =>
            durability is null
                ? StorageDurabilityDescriptor.Unknown(provider)
                : new StorageDurabilityDescriptor(durability.Value, provider);
    }

    private sealed class FakeProjectionStore(StorageDurability? durability, string provider)
        : SilentProjectionStore, IStorageDurabilityDescriptorProvider
    {
        public StorageDurabilityDescriptor DescribeStorage() =>
            durability is null
                ? StorageDurabilityDescriptor.Unknown(provider)
                : new StorageDurabilityDescriptor(durability.Value, provider);
    }

    /// <summary>Same idea as SilentEventStore, for the projection state store.</summary>
    private class SilentProjectionStore : IMultiProjectionStateStore
    {
        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
            string projectorName,
            string projectorVersion,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(
            string projectorName,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ResultBox<bool>> UpsertAsync(
            MultiProjectionStateRecord record,
            int offloadThresholdBytes = 1_000_000,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ResultBox<bool>> DeleteAsync(
            string projectorName,
            string projectorVersion,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ResultBox<int>> DeleteAllAsync(
            string? projectorName = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>None of these members run: the guard describes what it resolved, it does not use it.</summary>
    private class SilentEventStore : IEventStore
    {
        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) =>
            throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) =>
            throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount) => throw new NotSupportedException();
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) =>
            throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => throw new NotSupportedException();
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => throw new NotSupportedException();
    }
}
