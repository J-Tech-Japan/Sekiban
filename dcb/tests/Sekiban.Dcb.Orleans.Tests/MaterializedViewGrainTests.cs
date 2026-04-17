using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Orleans.Streams;
using Orleans.TestingHost;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Orleans;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.ServiceId;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class MaterializedViewGrainTests : IAsyncLifetime
{
    private static readonly FakeMvRegistryStore SharedRegistry = new();
    private static readonly FakeMvExecutor SharedExecutor = new(SharedRegistry);

    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        SharedRegistry.Reset();
        SharedExecutor.Reset();
        SharedExecutor.SeedInitial(CreateSerializableEvent(1, DateTime.UtcNow.AddSeconds(-5)));

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Options.ClusterId = $"mv-grain-{Guid.NewGuid():N}";
        builder.Options.ServiceId = $"mv-grain-{Guid.NewGuid():N}";
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TestClientConfigurator>();

        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public async Task MaterializedViewGrain_Should_CatchUp_Then_Process_Stream_Events()
    {
        var grainKey = MvGrainKey.Build(DefaultServiceIdProvider.DefaultServiceId, TestMaterializedViewProjector.ViewNameConst, 1);
        var grain = _cluster.Client.GetGrain<IMaterializedViewGrain>(grainKey);

        await WaitUntilAsync(async () =>
        {
            var status = await grain.GetStatusAsync();
            return status.CurrentPosition == SharedExecutor.InitialEvents[0].SortableUniqueIdValue;
        });

        var streamedEvent = CreateSerializableEvent(2, DateTime.UtcNow);
        var stream = _cluster.Client
            .GetStreamProvider("EventStreamProvider")
            .GetStream<SerializableEvent>(StreamId.Create("AllEvents", Guid.Empty));
        await stream.OnNextAsync(streamedEvent);

        await WaitUntilAsync(() => grain.IsSortableUniqueIdReceived(streamedEvent.SortableUniqueIdValue));

        var statusAfterStream = await grain.GetStatusAsync();
        Assert.True(statusAfterStream.Started);
        Assert.True(statusAfterStream.SubscriptionActive);
        Assert.Equal(streamedEvent.SortableUniqueIdValue, statusAfterStream.CurrentPosition);
        Assert.Contains(streamedEvent.Id, SharedExecutor.AppliedEventIds);
    }

    [Fact]
    public async Task MvOrleansQueryAccessor_Should_Return_Runtime_Context()
    {
        var accessor = new MvOrleansQueryAccessor(
            _cluster.Client,
            SharedRegistry,
            new DefaultServiceIdProvider(),
            new MvStorageInfoProvider(new MvStorageInfo(MvDbType.Postgres, "Host=test;Database=mv;")));

        var context = await accessor.GetAsync(new TestMaterializedViewProjector());
        var table = context.GetRequiredTable("main");

        Assert.Equal(DefaultServiceIdProvider.DefaultServiceId, context.ServiceId);
        Assert.Equal(MvDbType.Postgres, context.DatabaseType);
        Assert.Equal("Host=test;Database=mv;", context.ConnectionString);
        Assert.True(context.Entries.Count > 0);
        Assert.Equal("main", table.LogicalTable);
    }

    private static SerializableEvent CreateSerializableEvent(int ordinal, DateTime timestampUtc) =>
        new(
            Payload: [],
            SortableUniqueIdValue: SortableUniqueId.Generate(timestampUtc, Guid.Parse($"00000000-0000-0000-0000-{ordinal:D12}")),
            Id: Guid.Parse($"10000000-0000-0000-0000-{ordinal:D12}"),
            EventMetadata: new EventMetadata("test-command", "test-user", "test"),
            Tags: [],
            EventPayloadName: $"TestEvent{ordinal}");

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, int timeoutMs = 5000, int pollMs = 50)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(pollMs);
        }

        Assert.Fail("Condition was not satisfied before timeout.");
    }

    private sealed class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSekibanDcbMaterializedView();
                    services.AddSingleton<IEventTypes>(_ => new SimpleEventTypes());
                    services.Configure<MvOptions>(options =>
                    {
                        options.PollInterval = TimeSpan.FromMilliseconds(20);
                        options.StreamReorderWindow = TimeSpan.FromMilliseconds(10);
                    });
                    services.AddMaterializedView<TestMaterializedViewProjector>();
                    services.AddSekibanDcbMaterializedViewOrleans();
                    services.AddSingleton<IMvRegistryStore>(SharedRegistry);
                    services.AddSingleton<IMvExecutor>(SharedExecutor);
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();
                    services.AddSingleton<IMvStorageInfoProvider>(
                        new MvStorageInfoProvider(new MvStorageInfo(MvDbType.Postgres, "Host=test;Database=mv;")));
                })
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    private sealed class TestClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddMemoryStreams("EventStreamProvider");
        }
    }

    private sealed class TestMaterializedViewProjector : IMaterializedViewProjector
    {
        public const string ViewNameConst = "TestMv";

        public string ViewName => ViewNameConst;
        public int ViewVersion => 1;

        public Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
            Event ev,
            IMvApplyContext ctx,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MvSqlStatement>>([]);
    }

    private sealed class FakeMvExecutor : IMvExecutor
    {
        private readonly FakeMvRegistryStore _registry;

        public FakeMvExecutor(FakeMvRegistryStore registry) => _registry = registry;

        public List<SerializableEvent> InitialEvents { get; } = [];
        public HashSet<Guid> AppliedEventIds { get; } = [];

        public void Reset()
        {
            InitialEvents.Clear();
            AppliedEventIds.Clear();
        }

        public void SeedInitial(params SerializableEvent[] events) => InitialEvents.AddRange(events);

        public Task InitializeAsync(
            IMvApplyHost host,
            string? serviceId = null,
            CancellationToken cancellationToken = default)
        {
            serviceId ??= DefaultServiceIdProvider.DefaultServiceId;
            return _registry.RegisterViewAsync(serviceId, host.ViewName, host.ViewVersion, cancellationToken);
        }

        public async Task<MvCatchUpResult> CatchUpOnceAsync(
            IMvApplyHost host,
            string? serviceId = null,
            CancellationToken cancellationToken = default)
        {
            serviceId ??= DefaultServiceIdProvider.DefaultServiceId;
            await InitializeAsync(host, serviceId, cancellationToken);

            var currentPosition = await _registry.GetCurrentPositionAsync(serviceId, host.ViewName, host.ViewVersion, cancellationToken);
            var batch = InitialEvents
                .Where(serializableEvent =>
                    string.IsNullOrWhiteSpace(currentPosition) ||
                    string.Compare(serializableEvent.SortableUniqueIdValue, currentPosition, StringComparison.Ordinal) > 0)
                .OrderBy(serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
                .Take(100)
                .ToList();

            if (batch.Count == 0)
            {
                return new MvCatchUpResult(0, false);
            }

            foreach (var serializableEvent in batch)
            {
                AppliedEventIds.Add(serializableEvent.Id);
            }

            await _registry.UpdatePositionAsync(
                new MvPositionUpdate(
                    serviceId,
                    host.ViewName,
                    host.ViewVersion,
                    batch[^1].SortableUniqueIdValue,
                    MvApplySource.CatchUp,
                    batch.Count),
                cancellationToken: cancellationToken);
            return new MvCatchUpResult(batch.Count, false, batch[^1].SortableUniqueIdValue);
        }

        public async Task<int> ApplySerializableEventsAsync(
            IMvApplyHost host,
            IReadOnlyList<SerializableEvent> events,
            string? serviceId = null,
            CancellationToken cancellationToken = default)
        {
            serviceId ??= DefaultServiceIdProvider.DefaultServiceId;
            await InitializeAsync(host, serviceId, cancellationToken);

            var currentPosition = await _registry.GetCurrentPositionAsync(serviceId, host.ViewName, host.ViewVersion, cancellationToken);
            var ordered = events
                .Where(serializableEvent =>
                    string.IsNullOrWhiteSpace(currentPosition) ||
                    string.Compare(serializableEvent.SortableUniqueIdValue, currentPosition, StringComparison.Ordinal) > 0)
                .OrderBy(serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
                .ToList();

            if (ordered.Count == 0)
            {
                return 0;
            }

            foreach (var serializableEvent in ordered)
            {
                AppliedEventIds.Add(serializableEvent.Id);
            }

            await _registry.UpdatePositionAsync(
                new MvPositionUpdate(
                    serviceId,
                    host.ViewName,
                    host.ViewVersion,
                    ordered[^1].SortableUniqueIdValue,
                    MvApplySource.Stream,
                    ordered.Count),
                cancellationToken: cancellationToken);
            return ordered.Count;
        }
    }

    private sealed class FakeMvRegistryStore : IMvRegistryStore
    {
        private readonly Dictionary<(string ServiceId, string ViewName, int ViewVersion, string LogicalTable), MvRegistryEntry> _entries = [];
        private readonly Dictionary<(string ServiceId, string ViewName), MvActiveEntry> _active = [];

        public void Reset()
        {
            _entries.Clear();
            _active.Clear();
        }

        public Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RegisterAsync(MvRegistryEntry entry, System.Data.IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            _entries[(entry.ServiceId, entry.ViewName, entry.ViewVersion, entry.LogicalTable)] = entry;
            return Task.CompletedTask;
        }

        public Task UpdatePositionAsync(
            MvPositionUpdate update,
            System.Data.IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default)
        {
            foreach (var key in _entries.Keys.Where(key =>
                         key.ServiceId == update.ServiceId &&
                         key.ViewName == update.ViewName &&
                         key.ViewVersion == update.ViewVersion).ToList())
            {
                var entry = _entries[key];
                _entries[key] = entry with
                {
                    CurrentPosition = update.SortableUniqueId,
                    LastSortableUniqueId = update.SortableUniqueId,
                    AppliedEventVersion = entry.AppliedEventVersion + update.AppliedEventVersionDelta,
                    LastAppliedSource = update.Source == MvApplySource.Stream ? "stream" : "catchup",
                    LastAppliedAt = DateTimeOffset.UtcNow,
                    LastStreamAppliedSortableUniqueId = update.Source == MvApplySource.Stream ? update.SortableUniqueId : entry.LastStreamAppliedSortableUniqueId,
                    LastCatchUpSortableUniqueId = update.Source == MvApplySource.CatchUp ? update.SortableUniqueId : entry.LastCatchUpSortableUniqueId,
                    LastUpdated = DateTimeOffset.UtcNow
                };
            }

            return Task.CompletedTask;
        }

        public Task MarkStreamReceivedAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            string sortableUniqueId,
            DateTimeOffset receivedAt,
            System.Data.IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default)
        {
            foreach (var key in _entries.Keys.Where(key => key.ServiceId == serviceId && key.ViewName == viewName && key.ViewVersion == viewVersion).ToList())
            {
                var entry = _entries[key];
                _entries[key] = entry with
                {
                    LastStreamReceivedSortableUniqueId = string.IsNullOrWhiteSpace(entry.LastStreamReceivedSortableUniqueId) ||
                                                         string.Compare(entry.LastStreamReceivedSortableUniqueId, sortableUniqueId, StringComparison.Ordinal) < 0
                        ? sortableUniqueId
                        : entry.LastStreamReceivedSortableUniqueId,
                    LastStreamReceivedAt = receivedAt,
                    LastUpdated = DateTimeOffset.UtcNow
                };
            }

            return Task.CompletedTask;
        }

        public Task UpdateStatusAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            MvStatus status,
            System.Data.IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default)
        {
            foreach (var key in _entries.Keys.Where(key => key.ServiceId == serviceId && key.ViewName == viewName && key.ViewVersion == viewVersion).ToList())
            {
                var entry = _entries[key];
                _entries[key] = entry with
                {
                    Status = status,
                    LastUpdated = DateTimeOffset.UtcNow
                };
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MvRegistryEntry>> GetEntriesAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<MvRegistryEntry> entries = _entries
                .Where(pair => pair.Key.ServiceId == serviceId && pair.Key.ViewName == viewName && pair.Key.ViewVersion == viewVersion)
                .Select(pair => pair.Value)
                .OrderBy(entry => entry.LogicalTable, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(entries);
        }

        public Task<MvActiveEntry?> GetActiveAsync(string serviceId, string viewName, CancellationToken cancellationToken = default)
        {
            _active.TryGetValue((serviceId, viewName), out var entry);
            return Task.FromResult(entry);
        }

        public Task SetActiveAsync(
            string serviceId,
            string viewName,
            int activeVersion,
            System.Data.IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default)
        {
            _active[(serviceId, viewName)] = new MvActiveEntry(serviceId, viewName, activeVersion, DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }

        public async Task RegisterViewAsync(string serviceId, string viewName, int viewVersion, CancellationToken cancellationToken)
        {
            var key = (serviceId, viewName, viewVersion, "main");
            if (_entries.ContainsKey(key))
            {
                return;
            }

            await RegisterAsync(
                new MvRegistryEntry
                {
                    ServiceId = serviceId,
                    ViewName = viewName,
                    ViewVersion = viewVersion,
                    LogicalTable = "main",
                    PhysicalTable = $"{viewName.ToLowerInvariant()}_main",
                    Status = MvStatus.CatchingUp,
                    AppliedEventVersion = 0,
                    LastUpdated = DateTimeOffset.UtcNow
                },
                cancellationToken: cancellationToken);
            await SetActiveAsync(serviceId, viewName, viewVersion, cancellationToken: cancellationToken);
        }

        public async Task<string?> GetCurrentPositionAsync(string serviceId, string viewName, int viewVersion, CancellationToken cancellationToken)
        {
            var entries = await GetEntriesAsync(serviceId, viewName, viewVersion, cancellationToken);
            return entries.Select(entry => entry.CurrentPosition).FirstOrDefault(position => !string.IsNullOrWhiteSpace(position));
        }
    }
}
