using System.Data;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView.MySql;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Tests;

public class MvProjectionStatusPublicationTests
{
    private const string ServiceId = "orders";
    private const string ViewName = "Order:Summary/日本語";
    private const int ViewVersion = 7;

    public static IEnumerable<object[]> ProductionMappingCases()
    {
        var nonzero = SortableUniqueId.Generate(DateTime.UnixEpoch.AddHours(1), Guid.Empty);
        yield return [MvCheckpointTruth.Unknown(), MvStatus.Active, ProjectionStatusPhases.Unknown, null!, false, false];
        yield return [MvCheckpointTruth.KnownZero(), MvStatus.Active, ProjectionStatusPhases.Active, SortableUniqueId.MinValue.Value, true, false];
        yield return [MvCheckpointTruth.Known(new SortableUniqueId(nonzero), MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp)), MvStatus.Active, ProjectionStatusPhases.Active, nonzero, true, false];
        yield return [MvCheckpointTruth.KnownZero(), MvStatus.Initializing, ProjectionStatusPhases.Starting, SortableUniqueId.MinValue.Value, false, false];
        yield return [MvCheckpointTruth.KnownZero(), MvStatus.CatchingUp, ProjectionStatusPhases.CatchingUp, SortableUniqueId.MinValue.Value, false, false];
        yield return [MvCheckpointTruth.KnownZero(), MvStatus.Ready, ProjectionStatusPhases.CaughtUp, SortableUniqueId.MinValue.Value, true, false];
        yield return [MvCheckpointTruth.KnownZero(), MvStatus.Retired, ProjectionStatusPhases.Stopped, SortableUniqueId.MinValue.Value, false, false];
        yield return [MvCheckpointTruth.KnownZero(), MvStatus.Faulted, ProjectionStatusPhases.Faulted, null!, false, true];
    }

    [Theory]
    [MemberData(nameof(ProductionMappingCases))]
    public async Task HostedWorker_MapsAuthoritativeTruthAndLifecycle_ThroughTypedAndSerializedG24Readers(
        MvCheckpointTruth truth,
        MvStatus status,
        string expectedPhase,
        string? expectedPosition,
        bool expectedCaughtUp,
        bool expectedFaulted)
    {
        var serviceProvider = new FixedServiceIdProvider(ServiceId);
        var statusStore = new ObservingStatusStore(serviceProvider);
        var registry = new StubRegistryStore(CreateEntry(truth, status));
        var executor = new ObservingExecutor(blockFirstCall: true);
        executor.StatusWriteCount = () => statusStore.UpsertCalls;
        var publisher = CreatePublisher(registry, statusStore);
        using var worker = CreateWorker(executor, publisher);

        await worker.StartAsync(CancellationToken.None);
        await executor.CatchUpEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, statusStore.UpsertCalls);
        executor.ContinueCatchUp.TrySetResult();
        await statusStore.Written.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        var identity = MvProjectionStatusIdentity.Create(ViewName, ViewVersion);
        var eventStore = new InMemoryEventStore(serviceProvider);
        var reader = new ProjectionStatusReader(
            statusStore,
            eventStore,
            serviceProvider,
            StatusOptions());
        var request = new ProjectionStatusReadRequest(ServiceId, identity.ProjectorName, identity.ProjectorVersion);
        var typed = await reader.ReadAsync(request);
        Assert.True(typed.IsSuccess, typed.IsSuccess ? null : typed.GetException().Message);
        var snapshot = Assert.Single(typed.GetValue());
        Assert.Equal(expectedPhase, snapshot.Phase);
        Assert.Equal(expectedPosition, snapshot.LastAppliedSortableUniqueId);
        Assert.Equal(expectedPosition, snapshot.LastTraversedSortableUniqueId);
        Assert.Equal(expectedCaughtUp, snapshot.IsCaughtUp);
        Assert.Equal(expectedFaulted, snapshot.IsFaulted);
        Assert.DoesNotContain("Password", snapshot.FaultMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var serializedReader = new SerializedProjectionStatusReader(reader, serviceProvider);
        var serialized = await serializedReader.AcceptAsync(SerializedProjectionStatusReader.SerializeRequest(request));
        Assert.True(serialized.IsSuccess, serialized.IsSuccess ? null : serialized.GetException().Message);
        var envelope = SerializedProjectionStatusReader.Deserialize(serialized.GetValue());
        Assert.True(envelope.IsSuccess, envelope.IsSuccess ? null : envelope.GetException().Message);
        var wireSnapshot = Assert.Single(envelope.GetValue().Snapshots);
        Assert.Equal(expectedPhase, wireSnapshot.Phase);
        Assert.Equal(expectedPosition, wireSnapshot.LastTraversedSortableUniqueId);
        Assert.Equal(expectedCaughtUp, wireSnapshot.IsCaughtUp);
    }

    [Theory]
    [InlineData("blocking-status")]
    [InlineData("blocking-registry")]
    [InlineData("throwing-registry")]
    public async Task HostedWorker_IsolatesSlowFailingPublication_WithBoundedTimeoutAndSecretFreeLogging(string failureMode)
    {
        const string secret = "Password=super-secret";
        var registry = new StubRegistryStore(CreateEntry(MvCheckpointTruth.KnownZero(), MvStatus.Active));
        registry.BlockReads = failureMode == "blocking-registry";
        registry.ThrowMessage = failureMode == "throwing-registry" ? secret : null;
        IProjectionStatusStore statusStore = failureMode == "blocking-status"
            ? new BlockingStatusStore()
            : new ObservingStatusStore(new FixedServiceIdProvider(ServiceId));
        var logger = new CapturingLogger<MvProjectionStatusPublisher>();
        var options = StatusOptions();
        options.HeartbeatWriteTimeout = TimeSpan.FromMilliseconds(30);
        options.HeartbeatInterval = TimeSpan.FromMilliseconds(1);
        var publisher = new MvProjectionStatusPublisher(registry, statusStore, options, logger);
        var executor = new ObservingExecutor(requiredCalls: 2);
        using var worker = CreateWorker(executor, publisher, TimeSpan.FromMilliseconds(1));

        await worker.StartAsync(CancellationToken.None);
        await executor.RequiredCallsReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.True(executor.CatchUpCalls >= 2);
        Assert.All(logger.Messages, message => Assert.DoesNotContain(secret, message, StringComparison.Ordinal));
        Assert.All(logger.Messages, message => Assert.DoesNotContain("super-secret", message, StringComparison.Ordinal));
    }

    [Fact]
    public void Identity_IsStableAndCollisionFreeForAmbiguousAndUnicodeNames()
    {
        var first = MvProjectionStatusIdentity.Create("a:b", 12);
        var second = MvProjectionStatusIdentity.Create("a", 312);
        var unicode = MvProjectionStatusIdentity.Create("注文/一覧", 12);

        Assert.Equal(first, MvProjectionStatusIdentity.Create("a:b", 12));
        Assert.NotEqual(first, second);
        Assert.NotEqual(first.ProjectorName, unicode.ProjectorName);
        Assert.Equal("v:12", first.ProjectorVersion);
    }

    [Fact]
    public async Task Publication_ValidatesExactServiceBeforeAnyIo_AndDoesNoRegistryIoWithoutSourceStatusStore()
    {
        var invalidRegistry = new StubRegistryStore(CreateEntry(MvCheckpointTruth.KnownZero(), MvStatus.Active));
        var invalidPublisher = CreatePublisher(
            invalidRegistry,
            new ObservingStatusStore(new FixedServiceIdProvider(ServiceId)));

        await Assert.ThrowsAsync<ArgumentException>(() => invalidPublisher.PublishIfDueAsync(
            " ", ViewName, ViewVersion, MvProjectionStatusPublisherKind.HostedWorker));
        Assert.Equal(0, invalidRegistry.GetEntriesCalls);

        var disabledRegistry = new StubRegistryStore(CreateEntry(MvCheckpointTruth.KnownZero(), MvStatus.Active));
        var disabledPublisher = new MvProjectionStatusPublisher(
            disabledRegistry,
            statusStore: null,
            StatusOptions(),
            NullLogger<MvProjectionStatusPublisher>.Instance);
        await disabledPublisher.PublishIfDueAsync(
            ServiceId, ViewName, ViewVersion, MvProjectionStatusPublisherKind.HostedWorker);
        Assert.Equal(0, disabledRegistry.GetEntriesCalls);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("mysql")]
    [InlineData("sqlserver")]
    [InlineData("sqlite")]
    public void AllRelationalTargets_ComposeWithTheSameSourceSideStatusPublisher(string provider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProjectionStatusStore>(new ObservingStatusStore(new FixedServiceIdProvider(ServiceId)));
        switch (provider)
        {
            case "postgres":
                services.AddSekibanDcbMaterializedViewPostgres("Host=unused", registerHostedWorker: false);
                break;
            case "mysql":
                services.AddSekibanDcbMaterializedViewMySql("Server=unused", registerHostedWorker: false);
                break;
            case "sqlserver":
                services.AddSekibanDcbMaterializedViewSqlServer("Server=unused", registerHostedWorker: false);
                break;
            default:
                services.AddSekibanDcbMaterializedViewSqlite("Data Source=:memory:", registerHostedWorker: false);
                break;
        }

        using var serviceProvider = services.BuildServiceProvider();
        Assert.NotNull(serviceProvider.GetRequiredService<MvProjectionStatusPublisher>());
    }

    [Fact]
    public void PublicWorkerConstructor_RemainsBinaryCompatible()
    {
        var legacy = typeof(MvCatchUpWorker).GetConstructor(
            [typeof(IMvApplyHostFactory), typeof(IMvExecutor), typeof(IOptions<MvOptions>), typeof(ILogger<MvCatchUpWorker>)]);
        var serviceBound = typeof(MvCatchUpWorker).GetConstructor(
            [typeof(IMvApplyHostFactory), typeof(IMvExecutor), typeof(IOptions<MvOptions>), typeof(ILogger<MvCatchUpWorker>), typeof(string)]);

        Assert.NotNull(legacy);
        Assert.NotNull(serviceBound);
    }

    [Fact]
    public void HostedWorkerDiConstructor_InjectsTheOffHotPathPublisher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMvRegistryStore>(new StubRegistryStore(CreateEntry(MvCheckpointTruth.KnownZero(), MvStatus.Active)));
        services.AddSingleton<IProjectionStatusStore>(new ObservingStatusStore(new FixedServiceIdProvider(ServiceId)));
        services.AddSingleton<IMvApplyHostFactory>(new StubHostFactory());
        services.AddSingleton<IMvExecutor>(new ObservingExecutor());
        services.AddSekibanDcbMaterializedView(options => options.ServiceId = ServiceId);
        services.AddSingleton<IHostedService, MvCatchUpWorker>();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<MvCatchUpWorker>(provider.GetRequiredService<IHostedService>());
        var selected = typeof(MvCatchUpWorker).GetConstructors().Single(constructor =>
            constructor.GetCustomAttributes(typeof(Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructorAttribute), false).Length == 1);
        Assert.Contains(selected.GetParameters(), parameter => parameter.ParameterType == typeof(MvProjectionStatusPublisher));
    }

    private static MvRegistryEntry CreateEntry(MvCheckpointTruth truth, MvStatus status) => new()
    {
        ServiceId = ServiceId,
        ViewName = ViewName,
        ViewVersion = ViewVersion,
        LogicalTable = "main",
        PhysicalTable = "unused",
        Status = status,
        CurrentPosition = truth.PositionValue,
        CurrentCheckpointTruth = truth,
        AppliedEventVersion = 41,
        LastUpdated = DateTimeOffset.UtcNow
    };

    private static ProjectionStatusOptions StatusOptions() => new()
    {
        ClusterId = "test-cluster",
        HeartbeatInterval = TimeSpan.FromMinutes(1),
        FreshnessWindow = TimeSpan.FromMinutes(2),
        SamplingWindow = TimeSpan.Zero,
        HeartbeatWriteTimeout = TimeSpan.FromSeconds(1)
    };

    private static MvProjectionStatusPublisher CreatePublisher(IMvRegistryStore registry, IProjectionStatusStore statusStore) =>
        new(registry, statusStore, StatusOptions(), NullLogger<MvProjectionStatusPublisher>.Instance);

    private static MvCatchUpWorker CreateWorker(
        ObservingExecutor executor,
        MvProjectionStatusPublisher publisher,
        TimeSpan? pollInterval = null) =>
        new(
            new StubHostFactory(),
            executor,
            Options.Create(new MvOptions
            {
                ServiceId = ServiceId,
                PollInterval = pollInterval ?? TimeSpan.FromMilliseconds(10),
                MaxConsecutiveFailuresBeforeStop = 10
            }),
            NullLogger<MvCatchUpWorker>.Instance,
            ServiceId,
            publisher);

    private sealed class StubHostFactory : IMvApplyHostFactory
    {
        public IReadOnlyList<MvApplyHostRegistration> GetRegistrations() => [new(ViewName, ViewVersion)];
        public IMvApplyHost Create(string viewName, int viewVersion) => new StubHost();
    }

    private sealed class StubHost : IMvApplyHost
    {
        public string ViewName => MvProjectionStatusPublicationTests.ViewName;
        public int ViewVersion => MvProjectionStatusPublicationTests.ViewVersion;
        public IReadOnlyList<string> LogicalTables => ["main"];
        public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(IMvTableBindings tables, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);
        public Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(SerializableEvent ev, IMvTableBindings tables, IMvApplyQueryPort queryPort, string sortableUniqueId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);
    }

    private sealed class ObservingExecutor : IMvExecutor
    {
        private readonly int _requiredCalls;
        private readonly bool _blockFirstCall;
        public ObservingExecutor(int requiredCalls = 1, bool blockFirstCall = false)
        {
            _requiredCalls = requiredCalls;
            _blockFirstCall = blockFirstCall;
        }
        public int CatchUpCalls { get; private set; }
        public int StatusWritesObservedInsideCatchUp { get; private set; }
        public Func<int>? StatusWriteCount { get; set; }
        public TaskCompletionSource RequiredCallsReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CatchUpEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueCatchUp { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task InitializeAsync(IMvApplyHost host, string? serviceId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task<MvCatchUpResult> CatchUpOnceAsync(IMvApplyHost host, string? serviceId = null, CancellationToken cancellationToken = default)
        {
            StatusWritesObservedInsideCatchUp = Math.Max(StatusWritesObservedInsideCatchUp, StatusWriteCount?.Invoke() ?? 0);
            CatchUpCalls++;
            CatchUpEntered.TrySetResult();
            if (_blockFirstCall && CatchUpCalls == 1)
            {
                await ContinueCatchUp.Task.WaitAsync(cancellationToken);
            }
            if (CatchUpCalls >= _requiredCalls)
            {
                RequiredCallsReached.TrySetResult();
            }
            return new MvCatchUpResult(0, false);
        }
        public Task<int> ApplySerializableEventsAsync(IMvApplyHost host, IReadOnlyList<SerializableEvent> events, string? serviceId = null, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class ObservingStatusStore : IProjectionStatusStore
    {
        private readonly InMemoryMultiProjectionStateStore _inner;
        public ObservingStatusStore(IServiceIdProvider serviceIdProvider) => _inner = new(serviceIdProvider);
        public TaskCompletionSource Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int UpsertCalls { get; private set; }
        public async Task<ResultBox<ProjectionStatusWriteResult>> UpsertAsync(ProjectionStatusHeartbeat heartbeat, long expectedSequence, CancellationToken cancellationToken = default)
        {
            UpsertCalls++;
            var result = await _inner.UpsertAsync(heartbeat, expectedSequence, cancellationToken);
            if (result.IsSuccess && result.GetValue().Committed) Written.TrySetResult();
            return result;
        }
        public Task<ResultBox<IReadOnlyList<ProjectionStatusHeartbeat>>> ListAsync(string? projectorName = null, string? projectorVersion = null, CancellationToken cancellationToken = default) =>
            _inner.ListAsync(projectorName, projectorVersion, cancellationToken);
    }

    private sealed class BlockingStatusStore : IProjectionStatusStore
    {
        private readonly TaskCompletionSource<ResultBox<ProjectionStatusWriteResult>> _never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ResultBox<ProjectionStatusWriteResult>> UpsertAsync(ProjectionStatusHeartbeat heartbeat, long expectedSequence, CancellationToken cancellationToken = default) => _never.Task;
        public Task<ResultBox<IReadOnlyList<ProjectionStatusHeartbeat>>> ListAsync(string? projectorName = null, string? projectorVersion = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(ResultBox.FromValue<IReadOnlyList<ProjectionStatusHeartbeat>>([]));
    }

    private sealed class StubRegistryStore(MvRegistryEntry entry) : IMvRegistryStore
    {
        private readonly TaskCompletionSource<IReadOnlyList<MvRegistryEntry>> _never =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int GetEntriesCalls { get; private set; }
        public bool BlockReads { get; set; }
        public string? ThrowMessage { get; set; }
        public Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RegisterAsync(MvRegistryEntry value, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdatePositionAsync(MvPositionUpdate update, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkStreamReceivedAsync(string serviceId, string viewName, int viewVersion, string sortableUniqueId, DateTimeOffset receivedAt, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateStatusAsync(string serviceId, string viewName, int viewVersion, MvStatus status, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<MvRegistryEntry>> GetEntriesAsync(string serviceId, string viewName, int viewVersion, CancellationToken cancellationToken = default)
        {
            GetEntriesCalls++;
            if (ThrowMessage is not null)
            {
                return Task.FromException<IReadOnlyList<MvRegistryEntry>>(new InvalidOperationException(ThrowMessage));
            }

            if (BlockReads)
            {
                return _never.Task;
            }

            return Task.FromResult<IReadOnlyList<MvRegistryEntry>>([entry]);
        }
        public Task<MvActiveEntry?> GetActiveAsync(string serviceId, string viewName, CancellationToken cancellationToken = default) => Task.FromResult<MvActiveEntry?>(null);
        public Task SetActiveAsync(string serviceId, string viewName, int activeVersion, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedServiceIdProvider(string serviceId) : IServiceIdProvider
    {
        public string GetCurrentServiceId() => serviceId;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
