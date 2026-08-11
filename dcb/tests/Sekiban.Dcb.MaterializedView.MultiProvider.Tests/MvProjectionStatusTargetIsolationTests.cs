using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans.Streams;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView.Orleans;
using Sekiban.Dcb.MaterializedView.MySql;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

[Collection(nameof(PostgresMvCollection))]
public sealed class PostgresMvStatusTargetIsolationTests(PostgresMvFixture fixture) : MvStatusTargetIsolationTests(fixture);

[Collection(nameof(MySqlMvCollection))]
public sealed class MySqlMvStatusTargetIsolationTests(MySqlMvFixture fixture) : MvStatusTargetIsolationTests(fixture);

[Collection(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvStatusTargetIsolationTests(SqlServerMvFixture fixture) : MvStatusTargetIsolationTests(fixture);

[Collection(nameof(SqliteMvCollection))]
public sealed class SqliteMvStatusTargetIsolationTests(SqliteMvFixture fixture) : MvStatusTargetIsolationTests(fixture);

public abstract class MvStatusTargetIsolationTests(MultiProviderFixtureBase fixture)
{
    [SkippableFact]
    public async Task HostedWorker_HeartbeatAndG24Readers_PerformZeroTargetIo()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Integration fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);

        var targetIo = new TargetProviderIoCounter();
        var registry = new CountingDelegatingMvRegistryStore(
            fixture.Services.GetRequiredService<IMvRegistryStore>(),
            targetIo);
        await AssertTargetCounterIsNonVacuousAsync(registry, targetIo).ConfigureAwait(false);
        var statusStore = new CountingProjectionStatusStore(targetIo);
        var publisher = CreatePublisher(statusStore);
        var options = fixture.Services.GetRequiredService<IOptions<MvOptions>>();
        var executor = new BoundaryResettingExecutor(CreateProviderExecutor(registry, options), targetIo);
        var hostFactory = CreateIsolationHostFactory();
        using var worker = new MvCatchUpWorker(
            hostFactory,
            executor,
            options,
            NullLogger<MvCatchUpWorker>.Instance,
            MultiProviderFixtureBase.ServiceId,
            publisher);

        await worker.StartAsync(CancellationToken.None).ConfigureAwait(false);
        await statusStore.Written.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        await worker.StopAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(0, statusStore.TargetCallsObservedAtUpsert);
        targetIo.Reset();
        await AssertReadableThroughExistingG24SurfacesAsync(statusStore, fixture.EventStore).ConfigureAwait(false);
        Assert.Equal(0, targetIo.FactoryResolutions);
        Assert.Equal(0, targetIo.OpenCalls);
        Assert.Equal(0, targetIo.QueryCalls);
        Assert.Equal(0, targetIo.ProviderCalls);
    }

    [SkippableFact]
    public async Task OrleansGrain_HeartbeatAndG24Readers_PerformZeroTargetIo()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Integration fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);

        var targetIo = new TargetProviderIoCounter();
        var registry = new CountingDelegatingMvRegistryStore(
            fixture.Services.GetRequiredService<IMvRegistryStore>(),
            targetIo);
        await AssertTargetCounterIsNonVacuousAsync(registry, targetIo).ConfigureAwait(false);
        var statusStore = new CountingProjectionStatusStore(targetIo);
        var hostFactory = CreateIsolationHostFactory();
        var options = fixture.Services.GetRequiredService<IOptions<MvOptions>>();
        OrleansTargetIsolationContext.Current = new(
            hostFactory,
            CreateProviderExecutor(registry, options),
            registry,
            fixture.Services.GetRequiredService<IMvStorageInfoProvider>(),
            options,
            statusStore);

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Options.ClusterId = $"mv-target-isolation-{Guid.NewGuid():N}";
        builder.Options.ServiceId = $"mv-target-isolation-{Guid.NewGuid():N}";
        builder.AddSiloBuilderConfigurator<OrleansTargetIsolationSiloConfigurator>();
        builder.AddClientBuilderConfigurator<OrleansTargetIsolationClientConfigurator>();
        using var cluster = builder.Build();
        try
        {
            await cluster.DeployAsync().ConfigureAwait(false);
            var registration = Assert.Single(hostFactory.GetRegistrations(), item => item.ViewVersion == 1);
            var grain = cluster.Client.GetGrain<IMaterializedViewGrain>(MvGrainKey.Build(
                MultiProviderFixtureBase.ServiceId,
                registration.ViewName,
                registration.ViewVersion));
            await grain.EnsureStartedAsync().ConfigureAwait(false);
            await grain.RefreshAsync().ConfigureAwait(false);

            var writesBefore = statusStore.UpsertCalls;
            targetIo.Reset();
            await statusStore.WaitForWriteAfterAsync(writesBefore, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await AssertReadableThroughExistingG24SurfacesAsync(statusStore, fixture.EventStore).ConfigureAwait(false);

            Assert.Equal(0, targetIo.FactoryResolutions);
            Assert.Equal(0, targetIo.OpenCalls);
            Assert.Equal(0, targetIo.QueryCalls);
            Assert.Equal(0, targetIo.ProviderCalls);
        }
        finally
        {
            await cluster.StopAllSilosAsync().ConfigureAwait(false);
            OrleansTargetIsolationContext.Current = null;
        }
    }

    [SkippableFact]
    public async Task OrleansCoordinator_ForcedReverse_IsDurableAndObservationPerformsZeroTargetIo()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Integration fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);

        var targetIo = new TargetProviderIoCounter();
        var registry = new CountingDelegatingMvRegistryStore(
            fixture.Services.GetRequiredService<IMvRegistryStore>(),
            targetIo);
        var statusStore = new CountingProjectionStatusStore(targetIo);
        var hostFactory = CreateIsolationHostFactory();
        OrleansTargetIsolationContext.Current = new(
            hostFactory,
            CreateProviderExecutor(registry, fixture.Services.GetRequiredService<IOptions<MvOptions>>()),
            registry,
            fixture.Services.GetRequiredService<IMvStorageInfoProvider>(),
            fixture.Services.GetRequiredService<IOptions<MvOptions>>(),
            statusStore);

        await registry.EnsureInfrastructureAsync().ConfigureAwait(false);
        var target = MvCheckpointTruth.KnownZero(MvCheckpointProvenance.AuthoritativeTargetCapture());
        foreach (var version in new[] { 1, 2 })
        {
            await registry.RegisterAsync(new MvRegistryEntry
            {
                ServiceId = MultiProviderFixtureBase.ServiceId,
                ViewName = TargetIsolationProjector.ViewNameConst,
                ViewVersion = version,
                LogicalTable = "proof",
                PhysicalTable = $"mv_target_isolation_v{version}_proof",
                Status = MvStatus.Ready,
                CurrentCheckpointTruth = version == 1
                    ? MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.ReadUnavailable)
                    : MvCheckpointTruth.KnownZero(MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp)),
                TargetCheckpointTruth = target,
                LastUpdated = DateTimeOffset.UtcNow
            }).ConfigureAwait(false);
        }
        await registry.SetActiveAsync(
            MultiProviderFixtureBase.ServiceId,
            TargetIsolationProjector.ViewNameConst,
            2).ConfigureAwait(false);

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Options.ClusterId = $"mv-generation-switch-{Guid.NewGuid():N}";
        builder.Options.ServiceId = $"mv-generation-switch-{Guid.NewGuid():N}";
        builder.AddSiloBuilderConfigurator<OrleansTargetIsolationSiloConfigurator>();
        builder.AddClientBuilderConfigurator<OrleansTargetIsolationClientConfigurator>();
        using var cluster = builder.Build();
        try
        {
            await cluster.DeployAsync().ConfigureAwait(false);
            var coordinator = cluster.Client.GetGrain<IMvGenerationCoordinatorGrain>(
                MvGenerationCoordinatorGrainKey.Build(
                    MultiProviderFixtureBase.ServiceId,
                    TargetIsolationProjector.ViewNameConst));

            var result = await coordinator.ForceReverseAsync(1, 2, 1, "operator retained-generation rollback")
                .ConfigureAwait(false);
            Assert.True(result.Succeeded, result.Message);
            var active = Assert.IsType<MvActiveGenerationStatus>(await coordinator.GetActiveAsync().ConfigureAwait(false));
            Assert.Equal(1, active.ActiveVersion);
            Assert.Equal((int)MvSwitchKind.Forced, active.SwitchKind);
            Assert.Equal("operator retained-generation rollback", active.SwitchReason);

            targetIo.Reset();
            await AssertReadableThroughExistingG24SurfacesAsync(statusStore, fixture.EventStore).ConfigureAwait(false);
            Assert.Equal(0, targetIo.FactoryResolutions);
            Assert.Equal(0, targetIo.OpenCalls);
            Assert.Equal(0, targetIo.QueryCalls);
            Assert.Equal(0, targetIo.ProviderCalls);
        }
        finally
        {
            await cluster.StopAllSilosAsync().ConfigureAwait(false);
            OrleansTargetIsolationContext.Current = null;
        }
    }

    private static MvProjectionStatusPublisher CreatePublisher(IProjectionStatusStore statusStore) =>
        new(statusStore, StatusOptions(), NullLogger<MvProjectionStatusPublisher>.Instance);

    private IMvApplyHostFactory CreateIsolationHostFactory() => new NativeMvApplyHostFactory(
        [new TargetIsolationProjector(1), new TargetIsolationProjector(2)],
        fixture.DomainTypes.EventTypes,
        fixture.Services.GetRequiredService<IMvStorageInfoProvider>());

    private async Task AssertTargetCounterIsNonVacuousAsync(
        CountingDelegatingMvRegistryStore registry,
        TargetProviderIoCounter counter)
    {
        await registry.EnsureInfrastructureAsync().ConfigureAwait(false);
        await registry.GetEntriesAsync(MultiProviderFixtureBase.ServiceId, TargetIsolationProjector.ViewNameConst, 1)
            .ConfigureAwait(false);
        Assert.True(counter.ProviderCalls > 0, "The delegating counter did not observe its real target-provider control I/O.");
        Assert.True(counter.OpenCalls > 0);
        Assert.True(counter.QueryCalls > 0);
        counter.Reset();
    }

    private IMvExecutor CreateProviderExecutor(IMvRegistryStore registry, IOptions<MvOptions> options)
    {
        var storage = fixture.Services.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo();
        return storage.DatabaseType switch
        {
            MvDbType.Postgres => new PostgresMvExecutor(
                fixture.EventStoreFactory, registry, options, NullLogger<PostgresMvExecutor>.Instance, storage.ConnectionString),
            MvDbType.MySql => new MySqlMvExecutor(
                fixture.EventStoreFactory, registry, options, NullLogger<MySqlMvExecutor>.Instance, storage.ConnectionString),
            MvDbType.SqlServer => new SqlServerMvExecutor(
                fixture.EventStoreFactory, registry, options, NullLogger<SqlServerMvExecutor>.Instance, storage.ConnectionString),
            MvDbType.Sqlite => new SqliteMvExecutor(
                fixture.EventStoreFactory, registry, options, NullLogger<SqliteMvExecutor>.Instance, storage.ConnectionString),
            _ => throw new NotSupportedException($"Database type '{storage.DatabaseType}' is not supported.")
        };
    }

    private static async Task AssertReadableThroughExistingG24SurfacesAsync(
        IProjectionStatusStore statusStore,
        IEventStore eventStore)
    {
        var serviceIdProvider = new FixedServiceIdProvider(MultiProviderFixtureBase.ServiceId);
        var reader = new ProjectionStatusReader(statusStore, eventStore, serviceIdProvider, StatusOptions());
        var identity = MvProjectionStatusIdentity.Create(
            TargetIsolationProjector.ViewNameConst,
            1);
        var request = new ProjectionStatusReadRequest(
            MultiProviderFixtureBase.ServiceId,
            identity.ProjectorName,
            identity.ProjectorVersion);
        var typed = await reader.ReadAsync(request).ConfigureAwait(false);
        Assert.True(typed.IsSuccess, typed.IsSuccess ? null : typed.GetException().Message);
        Assert.Single(typed.GetValue());

        var serialized = new SerializedProjectionStatusReader(reader, serviceIdProvider);
        var response = await serialized.AcceptAsync(SerializedProjectionStatusReader.SerializeRequest(request))
            .ConfigureAwait(false);
        Assert.True(response.IsSuccess, response.IsSuccess ? null : response.GetException().Message);
        Assert.True(SerializedProjectionStatusReader.Deserialize(response.GetValue()).IsSuccess);
    }

    internal static ProjectionStatusOptions StatusOptionsForSilo() => new()
    {
        ClusterId = "mv-target-isolation",
        HeartbeatInterval = TimeSpan.FromMilliseconds(25),
        HeartbeatWriteTimeout = TimeSpan.FromSeconds(1),
        FreshnessWindow = TimeSpan.FromMinutes(1),
        SamplingWindow = TimeSpan.Zero
    };

    private static ProjectionStatusOptions StatusOptions() => StatusOptionsForSilo();

    private sealed class BoundaryResettingExecutor(IMvExecutor inner, TargetProviderIoCounter targetIo) : IMvExecutor
    {
        public Task InitializeAsync(IMvApplyHost host, string? serviceId = null, CancellationToken cancellationToken = default) =>
            inner.InitializeAsync(host, serviceId, cancellationToken);

        public async Task<MvCatchUpResult> CatchUpOnceAsync(
            IMvApplyHost host,
            string? serviceId = null,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.CatchUpOnceAsync(host, serviceId, cancellationToken).ConfigureAwait(false);
            targetIo.Reset();
            return result;
        }

        public Task<int> ApplySerializableEventsAsync(
            IMvApplyHost host,
            IReadOnlyList<SerializableEvent> events,
            string? serviceId = null,
            CancellationToken cancellationToken = default) =>
            inner.ApplySerializableEventsAsync(host, events, serviceId, cancellationToken);
    }

    private sealed class FixedServiceIdProvider(string serviceId) : IServiceIdProvider
    {
        public string GetCurrentServiceId() => serviceId;
    }

    private sealed class TargetIsolationProjector(int version) : IMaterializedViewProjector
    {
        public const string ViewNameConst = "MvStatusTargetIsolation";
        public string ViewName => ViewNameConst;
        public int ViewVersion => version;

        public async Task InitializeAsync(IMvInitContext context, CancellationToken cancellationToken = default)
        {
            var table = context.RegisterTable("proof");
            var sql = context.DatabaseType switch
            {
                MvDbType.Postgres or MvDbType.MySql or MvDbType.Sqlite =>
                    $"CREATE TABLE IF NOT EXISTS {table.PhysicalName} (id INTEGER NOT NULL PRIMARY KEY);",
                MvDbType.SqlServer =>
                    $"IF OBJECT_ID(N'{table.PhysicalName}', N'U') IS NULL CREATE TABLE {table.PhysicalName} (id INT NOT NULL PRIMARY KEY);",
                _ => throw new NotSupportedException($"Database type '{context.DatabaseType}' is not supported.")
            };
            await context.ExecuteAsync(sql, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
            Sekiban.Dcb.Events.Event ev,
            IMvApplyContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MvSqlStatement>>([]);
    }
}

internal sealed class CountingProjectionStatusStore(TargetProviderIoCounter targetIo) : IProjectionStatusStore
{
    private readonly InMemoryMultiProjectionStateStore _inner =
        new(new FixedStatusServiceIdProvider(MultiProviderFixtureBase.ServiceId));
    private TaskCompletionSource _written = NewSignal();

    public TaskCompletionSource Written => _written;
    public int UpsertCalls { get; private set; }
    public int TargetCallsObservedAtUpsert { get; private set; }

    public async Task<ResultBox<ProjectionStatusWriteResult>> UpsertAsync(
        ProjectionStatusHeartbeat heartbeat,
        long expectedSequence,
        CancellationToken cancellationToken = default)
    {
        TargetCallsObservedAtUpsert = Math.Max(TargetCallsObservedAtUpsert, targetIo.ProviderCalls);
        var result = await _inner.UpsertAsync(heartbeat, expectedSequence, cancellationToken).ConfigureAwait(false);
        UpsertCalls++;
        _written.TrySetResult();
        return result;
    }

    public Task<ResultBox<IReadOnlyList<ProjectionStatusHeartbeat>>> ListAsync(
        string? projectorName = null,
        string? projectorVersion = null,
        CancellationToken cancellationToken = default) =>
        _inner.ListAsync(projectorName, projectorVersion, cancellationToken);

    public async Task WaitForWriteAfterAsync(int count, TimeSpan timeout)
    {
        while (UpsertCalls <= count)
        {
            var signal = _written;
            if (UpsertCalls > count)
            {
                return;
            }
            await signal.Task.WaitAsync(timeout).ConfigureAwait(false);
            if (ReferenceEquals(signal, _written))
            {
                _written = NewSignal();
            }
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class FixedStatusServiceIdProvider(string serviceId) : IServiceIdProvider
    {
        public string GetCurrentServiceId() => serviceId;
    }
}

internal sealed class TargetProviderIoCounter
{
    public int FactoryResolutions { get; set; }
    public int OpenCalls { get; set; }
    public int QueryCalls { get; set; }
    public int ProviderCalls { get; set; }

    public void Reset() => (FactoryResolutions, OpenCalls, QueryCalls, ProviderCalls) = (0, 0, 0, 0);
}

/// <summary>
/// Transparent counter over the concrete provider registry. Every operation delegates to the real target store;
/// unlike a fake store, provider SQL, connection behavior, and failures remain in the exercised path.
/// </summary>
internal sealed class CountingDelegatingMvRegistryStore : IMvRegistryStore
{
    private readonly IMvRegistryStore _inner;
    private readonly TargetProviderIoCounter _counter;

    public CountingDelegatingMvRegistryStore(IMvRegistryStore inner, TargetProviderIoCounter counter)
    {
        _inner = inner;
        _counter = counter;
        _counter.FactoryResolutions++;
    }

    public Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default)
    {
        Count();
        return _inner.EnsureInfrastructureAsync(cancellationToken);
    }

    public Task RegisterAsync(
        MvRegistryEntry entry,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        Count();
        return _inner.RegisterAsync(entry, transaction, cancellationToken);
    }

    public Task UpdatePositionAsync(
        MvPositionUpdate update,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        Count();
        return _inner.UpdatePositionAsync(update, transaction, cancellationToken);
    }

    public Task MarkStreamReceivedAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        string sortableUniqueId,
        DateTimeOffset receivedAt,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        Count();
        return _inner.MarkStreamReceivedAsync(
            serviceId, viewName, viewVersion, sortableUniqueId, receivedAt, transaction, cancellationToken);
    }

    public Task UpdateStatusAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        MvStatus status,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        Count();
        return _inner.UpdateStatusAsync(serviceId, viewName, viewVersion, status, transaction, cancellationToken);
    }

    public Task<IReadOnlyList<MvRegistryEntry>> GetEntriesAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        CancellationToken cancellationToken = default)
    {
        Count(query: true);
        return _inner.GetEntriesAsync(serviceId, viewName, viewVersion, cancellationToken);
    }

    public Task<MvActiveEntry?> GetActiveAsync(
        string serviceId,
        string viewName,
        CancellationToken cancellationToken = default)
    {
        Count(query: true);
        return _inner.GetActiveAsync(serviceId, viewName, cancellationToken);
    }

    public Task SetTargetCheckpointAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        MvCheckpointTruth targetCheckpointTruth,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        Count();
        return _inner.SetTargetCheckpointAsync(
            serviceId, viewName, viewVersion, targetCheckpointTruth, transaction, cancellationToken);
    }

    public Task<MvActivationResult> TryActivateAsync(
        MvActivationRequest request,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        Count(query: true);
        return _inner.TryActivateAsync(request, transaction, cancellationToken);
    }

    public Task<MvActivationResult> TryForceReverseAsync(
        MvForcedReverseRequest request,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        Count(query: true);
        return _inner.TryForceReverseAsync(request, transaction, cancellationToken);
    }

    public Task SetActiveAsync(
        string serviceId,
        string viewName,
        int activeVersion,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        Count();
        return _inner.SetActiveAsync(serviceId, viewName, activeVersion, transaction, cancellationToken);
    }

    private void Count(bool query = false)
    {
        _counter.ProviderCalls++;
        _counter.OpenCalls++;
        if (query)
        {
            _counter.QueryCalls++;
        }
    }
}

internal sealed record OrleansTargetIsolationContext(
    IMvApplyHostFactory HostFactory,
    IMvExecutor Executor,
    IMvRegistryStore Registry,
    IMvStorageInfoProvider StorageInfo,
    IOptions<MvOptions> Options,
    CountingProjectionStatusStore StatusStore)
{
    public static OrleansTargetIsolationContext? Current { get; set; }
}

internal sealed class OrleansTargetIsolationSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        var context = OrleansTargetIsolationContext.Current ??
            throw new InvalidOperationException("Target-isolation context was not initialized.");
        siloBuilder.ConfigureServices(services =>
            {
                services.AddSingleton(context.HostFactory);
                services.AddSingleton(context.Executor);
                services.AddSingleton(context.Registry);
                services.AddSingleton(context.StorageInfo);
                services.AddSingleton(context.Options);
                services.AddSingleton<IProjectionStatusStore>(context.StatusStore);
                services.AddSingleton(MvStatusTargetIsolationTests.StatusOptionsForSilo());
                services.AddSingleton<MvProjectionStatusPublisher>();
                services.AddSingleton<IMvGenerationCoordinator, MvGenerationCoordinator>();
                services.AddSingleton<IServiceIdProvider>(new FixedOrleansServiceIdProvider(MultiProviderFixtureBase.ServiceId));
                services.AddSingleton<IEventSubscriptionResolver>(
                    new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                services.AddSekibanDcbMaterializedViewOrleans(activateOnStartup: false);
            })
            .AddMemoryGrainStorage("PubSubStore")
            .AddMemoryStreams("EventStreamProvider")
            .AddMemoryGrainStorage("EventStreamProvider");
    }

    private sealed class FixedOrleansServiceIdProvider(string serviceId) : IServiceIdProvider
    {
        public string GetCurrentServiceId() => serviceId;
    }
}

internal sealed class OrleansTargetIsolationClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) =>
        clientBuilder.AddMemoryStreams("EventStreamProvider");
}
