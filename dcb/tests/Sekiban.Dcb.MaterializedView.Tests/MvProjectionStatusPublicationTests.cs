using System.Diagnostics;
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
        var snapshotToPublish = new MvProjectionStatusSnapshot(truth, status, 41);
        var executor = new ObservingExecutor(snapshotToPublish, blockFirstCall: true);
        executor.StatusWriteCount = () => statusStore.UpsertCalls;
        var publisher = CreatePublisher(statusStore);
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
    [InlineData("throwing-status")]
    public async Task HostedWorker_IsolatesSlowFailingPublication_WithBoundedTimeoutAndSecretFreeLogging(string failureMode)
    {
        const string secret = "Password=super-secret";
        IProjectionStatusStore statusStore = failureMode == "blocking-status"
            ? new BlockingStatusStore()
            : new ThrowingStatusStore(secret);
        var logger = new CapturingLogger<MvProjectionStatusPublisher>();
        var options = StatusOptions();
        options.HeartbeatWriteTimeout = TimeSpan.FromMilliseconds(30);
        options.HeartbeatInterval = TimeSpan.FromMilliseconds(1);
        var publisher = new MvProjectionStatusPublisher(statusStore, options, logger);
        var executor = new ObservingExecutor(
            new MvProjectionStatusSnapshot(MvCheckpointTruth.KnownZero(), MvStatus.Active, 0),
            requiredCalls: 2);
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
    public async Task Publisher_RebasesExternalRowLossAndCompetingCreate_UsingTheRealStoreContract()
    {
        // Exercise the production publisher against the actual in-memory status store. This proves both that an absent
        // expected>0 update does not create in the same operation and that the later expected=0 create remains conditional
        // when another writer wins the race.
        var serviceIdProvider = new FixedServiceIdProvider(ServiceId);
        var store = new InMemoryMultiProjectionStateStore(serviceIdProvider);
        var publisher = new MvProjectionStatusPublisher(
            store,
            StatusOptions(),
            NullLogger<MvProjectionStatusPublisher>.Instance);
        var snapshot = new MvProjectionStatusSnapshot(MvCheckpointTruth.KnownZero(), MvStatus.Active, 1);

        await publisher.PublishSwitchAsync(
            ServiceId,
            ViewName,
            ViewVersion,
            snapshot,
            MvProjectionStatusPublisherKind.HostedWorker);
        Assert.Equal(1, Assert.Single((await store.ListAsync()).GetValue()).Sequence);

        // The second production write has expected=1. Removing the row must leave the store empty after that write.
        store.Clear();
        await publisher.PublishSwitchAsync(
            ServiceId,
            ViewName,
            ViewVersion,
            snapshot,
            MvProjectionStatusPublisherKind.HostedWorker);
        Assert.Empty((await store.ListAsync()).GetValue());

        var identity = MvProjectionStatusIdentity.Create(ViewName, ViewVersion);
        var competingCreate = new ProjectionStatusHeartbeat(
            ServiceId,
            identity.ProjectorName,
            identity.ProjectorVersion,
            "test-cluster:mv:hosted",
            "competing-activation",
            1,
            1,
            null,
            null,
            DateTimeOffset.UtcNow)
        {
            Phase = ProjectionStatusPhases.Active
        };
        Assert.True((await store.UpsertAsync(competingCreate, 0)).GetValue().Committed);

        // The publisher's conditional create loses, observes RowAlreadyExists, and only rebases its fence.
        await publisher.PublishSwitchAsync(
            ServiceId,
            ViewName,
            ViewVersion,
            snapshot,
            MvProjectionStatusPublisherKind.HostedWorker);
        var afterConflict = Assert.Single((await store.ListAsync()).GetValue());
        Assert.Equal("competing-activation", afterConflict.ActivationId);
        Assert.Equal(1, afterConflict.Sequence);

        // A later operation uses the rebased expected=1 update route and advances normally.
        await publisher.PublishSwitchAsync(
            ServiceId,
            ViewName,
            ViewVersion,
            snapshot,
            MvProjectionStatusPublisherKind.HostedWorker);
        Assert.Equal(2, Assert.Single((await store.ListAsync()).GetValue()).Sequence);
    }

    [Fact]
    public async Task Publication_ValidatesExactServiceBeforeAnyIo_AndDoesNoStatusIoWhenDisabled()
    {
        var invalidStore = new ObservingStatusStore(new FixedServiceIdProvider(ServiceId));
        var invalidPublisher = CreatePublisher(invalidStore);
        var snapshot = new MvProjectionStatusSnapshot(MvCheckpointTruth.KnownZero(), MvStatus.Active, 0);

        await Assert.ThrowsAsync<ArgumentException>(() => invalidPublisher.PublishIfDueAsync(
            " ", ViewName, ViewVersion, snapshot, MvProjectionStatusPublisherKind.HostedWorker));
        Assert.Equal(0, invalidStore.UpsertCalls);

        var disabledPublisher = new MvProjectionStatusPublisher(
            statusStore: null,
            options: StatusOptions(),
            logger: NullLogger<MvProjectionStatusPublisher>.Instance);
        await disabledPublisher.PublishIfDueAsync(
            ServiceId, ViewName, ViewVersion, snapshot, MvProjectionStatusPublisherKind.HostedWorker);
    }

    [Fact]
    public async Task SwitchAudit_RemainsOnSubsequentCadence_AndOrdinarySwitchClearsForcedReason()
    {
        var store = new ObservingStatusStore(new FixedServiceIdProvider(ServiceId));
        var options = StatusOptions();
        options.HeartbeatInterval = TimeSpan.Zero;
        var publisher = new MvProjectionStatusPublisher(store, options, NullLogger<MvProjectionStatusPublisher>.Instance);
        var now = DateTimeOffset.UtcNow;
        var progress = new MvProjectionStatusSnapshot(MvCheckpointTruth.KnownZero(), MvStatus.Active, 1);

        await publisher.PublishSwitchAsync(
            ServiceId,
            ViewName,
            ViewVersion,
            progress with { SwitchKind = MvSwitchKind.Forced, SwitchReason = "operator rollback", SwitchedAtUtc = now },
            MvProjectionStatusPublisherKind.HostedWorker);
        await publisher.PublishIfDueAsync(
            ServiceId, ViewName, ViewVersion, progress, MvProjectionStatusPublisherKind.HostedWorker);
        var afterCadence = Assert.Single((await store.ListAsync()).GetValue());
        Assert.Equal("forced", afterCadence.SwitchKind);
        Assert.Equal("operator rollback", afterCadence.SwitchReason);

        await publisher.PublishSwitchAsync(
            ServiceId,
            ViewName,
            ViewVersion,
            progress with { SwitchKind = MvSwitchKind.Forward, SwitchedAtUtc = now.AddSeconds(1) },
            MvProjectionStatusPublisherKind.HostedWorker);
        await publisher.PublishIfDueAsync(
            ServiceId, ViewName, ViewVersion, progress, MvProjectionStatusPublisherKind.HostedWorker);
        var afterOrdinary = Assert.Single((await store.ListAsync()).GetValue());
        Assert.Equal("forward", afterOrdinary.SwitchKind);
        Assert.Null(afterOrdinary.SwitchReason);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("mysql")]
    [InlineData("sqlserver")]
    [InlineData("sqlite")]
    public async Task AllRelationalTargets_RunHostedCadenceAndG24Readers_WithoutResolvingTargetProvider(string provider)
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

        var targetDescriptor = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IMvRegistryStore));
        services.Remove(targetDescriptor);
        using var targetIo = new TargetProviderIoCounter();
        services.AddSingleton<IMvRegistryStore>(sp =>
        {
            targetIo.FactoryResolutions++;
            return (IMvRegistryStore)(targetDescriptor.ImplementationFactory?.Invoke(sp) ??
                throw new InvalidOperationException("Provider registry descriptor must use its production factory."));
        });

        using var serviceProvider = services.BuildServiceProvider();
        var statusStore = Assert.IsType<ObservingStatusStore>(serviceProvider.GetRequiredService<IProjectionStatusStore>());
        var publisher = serviceProvider.GetRequiredService<MvProjectionStatusPublisher>();
        var executor = new ObservingExecutor(
            new MvProjectionStatusSnapshot(MvCheckpointTruth.KnownZero(), MvStatus.Active, 0));
        using var worker = CreateWorker(executor, publisher);
        await worker.StartAsync(CancellationToken.None);
        await statusStore.Written.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        var serviceIdProvider = new FixedServiceIdProvider(ServiceId);
        var reader = new ProjectionStatusReader(
            statusStore,
            new InMemoryEventStore(serviceIdProvider),
            serviceIdProvider,
            StatusOptions());
        var identity = MvProjectionStatusIdentity.Create(ViewName, ViewVersion);
        var request = new ProjectionStatusReadRequest(ServiceId, identity.ProjectorName, identity.ProjectorVersion);
        Assert.Single((await reader.ReadAsync(request)).GetValue());
        var serialized = new SerializedProjectionStatusReader(reader, serviceIdProvider);
        Assert.True((await serialized.AcceptAsync(SerializedProjectionStatusReader.SerializeRequest(request))).IsSuccess);
        Assert.Equal(0, targetIo.FactoryResolutions);
        Assert.Equal(0, targetIo.OpenCalls);
        Assert.Equal(0, targetIo.QueryCalls);
        Assert.Equal(0, targetIo.ProviderCalls);
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
        services.AddSingleton<IProjectionStatusStore>(new ObservingStatusStore(new FixedServiceIdProvider(ServiceId)));
        services.AddSingleton<IMvApplyHostFactory>(new StubHostFactory());
        services.AddSingleton<IMvExecutor>(new ObservingExecutor(MvProjectionStatusSnapshot.Unknown()));
        services.AddSekibanDcbMaterializedView(options => options.ServiceId = ServiceId);
        services.AddSingleton<IHostedService, MvCatchUpWorker>();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<MvCatchUpWorker>(provider.GetRequiredService<IHostedService>());
        var selected = typeof(MvCatchUpWorker).GetConstructors().Single(constructor =>
            constructor.GetCustomAttributes(typeof(Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructorAttribute), false).Length == 1);
        Assert.Contains(selected.GetParameters(), parameter => parameter.ParameterType == typeof(MvProjectionStatusPublisher));
    }

    private static ProjectionStatusOptions StatusOptions() => new()
    {
        ClusterId = "test-cluster",
        HeartbeatInterval = TimeSpan.FromMinutes(1),
        FreshnessWindow = TimeSpan.FromMinutes(2),
        SamplingWindow = TimeSpan.Zero,
        HeartbeatWriteTimeout = TimeSpan.FromSeconds(1)
    };

    private static MvProjectionStatusPublisher CreatePublisher(IProjectionStatusStore statusStore) =>
        new(statusStore, StatusOptions(), NullLogger<MvProjectionStatusPublisher>.Instance);

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
        private readonly MvProjectionStatusSnapshot _snapshot;
        private readonly int _requiredCalls;
        private readonly bool _blockFirstCall;
        public ObservingExecutor(
            MvProjectionStatusSnapshot snapshot,
            int requiredCalls = 1,
            bool blockFirstCall = false)
        {
            _snapshot = snapshot;
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
            return new MvCatchUpResult(0, false) { ProjectionStatus = _snapshot };
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

    private sealed class ThrowingStatusStore(string message) : IProjectionStatusStore
    {
        public Task<ResultBox<ProjectionStatusWriteResult>> UpsertAsync(
            ProjectionStatusHeartbeat heartbeat,
            long expectedSequence,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ResultBox<ProjectionStatusWriteResult>>(new InvalidOperationException(message));

        public Task<ResultBox<IReadOnlyList<ProjectionStatusHeartbeat>>> ListAsync(
            string? projectorName = null,
            string? projectorVersion = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResultBox.FromValue<IReadOnlyList<ProjectionStatusHeartbeat>>([]));
    }

    private sealed class TargetProviderIoCounter : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _allListeners;

        public TargetProviderIoCounter() => _allListeners = DiagnosticListener.AllListeners.Subscribe(this);

        public int FactoryResolutions { get; set; }
        public int OpenCalls { get; private set; }
        public int QueryCalls { get; private set; }
        public int ProviderCalls { get; private set; }

        public void OnNext(DiagnosticListener value)
        {
            if (value.Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
                value.Name.Contains("MySql", StringComparison.OrdinalIgnoreCase) ||
                value.Name.Contains("SqlClient", StringComparison.OrdinalIgnoreCase) ||
                value.Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                _subscriptions.Add(value.Subscribe(this));
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            ProviderCalls++;
            if (value.Key.Contains("Open", StringComparison.OrdinalIgnoreCase))
            {
                OpenCalls++;
            }

            if (value.Key.Contains("Command", StringComparison.OrdinalIgnoreCase) ||
                value.Key.Contains("Execute", StringComparison.OrdinalIgnoreCase))
            {
                QueryCalls++;
            }
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
            _allListeners.Dispose();
        }
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
