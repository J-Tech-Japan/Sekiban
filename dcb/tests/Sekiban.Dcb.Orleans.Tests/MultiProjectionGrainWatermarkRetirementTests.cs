using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Production activation-path tests for the integrity-watermark retirement contract. The tests use the real
///     Orleans grain, activation restore path, enumerable catch-up path, coordinated provider writes, and external
///     checkpoint store. They deliberately do not invoke the private persist guard or decision function in isolation.
/// </summary>
[Collection("watermark-retirement")]
public sealed class MultiProjectionGrainWatermarkRetirementTests : IAsyncLifetime
{
    private static WatermarkHarness? ActiveHarness;
    private readonly WatermarkHarness _harness = new();
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        _harness.Reset();
        ActiveHarness = _harness;
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var id = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"G38-wm-{id}";
        builder.Options.ServiceId = $"G38-wm-{id}";
        var portBase = 30_000 + (Environment.ProcessId % 4_000) * 4;
        builder.PortAllocator = new FixedPortAllocator(portBase, portBase + 1);
        builder.Options.BaseSiloPort = portBase;
        builder.Options.BaseGatewayPort = portBase + 1;
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public async Task Known_present_restore_failure_retires_watermark_and_durably_converges_below_old_w()
    {
        const string projectorName = "g38-convergence";
        var events = BuildEvents(5_000);
        var seed = await _harness.SeedKnownPresentCheckpointAsync(projectorName, oldSafeVersion: 10_000, events);
        _harness.RestoreBehavior = RestoreBehavior.ResultBoxFailure;

        var grain = _cluster.Client.GetGrain<IMultiProjectionGrain>(projectorName);
        await grain.GetStatusAsync();
        await WaitUntilAsync(
            () => _harness.StateStore.Records.Any(record => record.EventsProcessed > 0),
            TimeSpan.FromSeconds(15),
            () => $"hosts={_harness.Hosts.Count}, writes={_harness.ProviderStorage.WriteCalls}, attempts={_harness.ProviderStorage.WriteAttempts}, upserts={_harness.StateStore.UpsertCalls}, records={_harness.StateStore.Records.Count}, reads={_harness.EventStore.ReadSinceValues.Count}");

        var intermediate = Assert.Single(
            _harness.StateStore.Records,
            record => record.EventsProcessed > 0);
        Assert.True(intermediate.EventsProcessed > 0);
        Assert.True(intermediate.EventsProcessed < seed.OldSafeVersion);
        Assert.Equal(intermediate.LastSortableUniqueId, _harness.StateStore.LatestRecord!.LastSortableUniqueId);

        var firstCheckpointHost = _harness.Hosts.First(host => host.AppliedEventIds.Count > 0);
        Assert.True(firstCheckpointHost.CompactionCalls > 0);
        Assert.Empty(firstCheckpointHost.NonIncrementalRetention);
        Assert.True(firstCheckpointHost.AppliedEventIds.Count > 0);

        // Force a genuine activation replacement. The replacement must restore the durable intermediate record and
        // start its authoritative read strictly after that record, rather than merely returning a persist success.
        _harness.RestoreBehavior = RestoreBehavior.None;
        await grain.RequestDeactivationAsync();

        var fresh = _cluster.Client.GetGrain<IMultiProjectionGrain>(projectorName);
        await fresh.GetStatusAsync();
        await WaitUntilAsync(() => _harness.Hosts.Count >= 2, TimeSpan.FromSeconds(10));
        await WaitUntilAsync(
            () => _harness.Hosts.Skip(1).Any(host => host.RestoreCalls > 0),
            TimeSpan.FromSeconds(10));

        var replacementHost = _harness.Hosts.Skip(1).First(host => host.RestoreCalls > 0);
        Assert.True(replacementHost.RestoreCalls > 0);
        Assert.Contains(
            _harness.EventStore.ReadSinceValues,
            since => string.Equals(since, intermediate.LastSortableUniqueId, StringComparison.Ordinal));
        Assert.NotEqual(seed.OldSafeVersion, intermediate.EventsProcessed);
        Assert.True(_harness.ProviderStorage.WriteCalls > 0);
        Assert.True((await fresh.GetHealthStatusAsync()).IsHealthy);
    }

    [Fact]
    public async Task Stream_open_and_host_restore_resultbox_failures_each_retire_all_four_fields()
    {
        await AssertResultBoxRetiresAllFourFieldsAsync(
            "g38-open-resultbox",
            StateStreamBehavior.ResultBoxFailure,
            RestoreBehavior.None);
        await AssertResultBoxRetiresAllFourFieldsAsync(
            "g38-restore-resultbox",
            StateStreamBehavior.Success,
            RestoreBehavior.ResultBoxFailure);
    }

    [Theory]
    [InlineData(StateStreamBehavior.Throw)]
    [InlineData(StateStreamBehavior.CanSeekThrows)]
    [InlineData(StateStreamBehavior.LengthThrows)]
    [InlineData(StateStreamBehavior.PositionThrows)]
    [InlineData(StateStreamBehavior.ReadThrows)]
    [InlineData(StateStreamBehavior.DisposeThrows)]
    public async Task Known_present_stream_and_restore_throws_retire_durably(StateStreamBehavior streamBehavior)
    {
        var projectorName = $"g38-throw-{streamBehavior}";
        await _harness.SeedKnownPresentCheckpointAsync(projectorName, oldSafeVersion: 700, BuildEvents(20));
        _harness.StreamBehavior = streamBehavior;
        _harness.RestoreBehavior = RestoreBehavior.None;

        var grain = _cluster.Client.GetGrain<IMultiProjectionGrain>(projectorName);
        await grain.GetStatusAsync();
        await WaitUntilAsync(
            () => _harness.ProviderStorage.CommittedStates.Any(IsRetiredState),
            TimeSpan.FromSeconds(10));
        await WaitUntilAsync(
            () => _harness.StateStore.Records.Any(record => record.EventsProcessed > 0),
            TimeSpan.FromSeconds(10));

        AssertRetiredCommittedState(projectorName);
        var intermediate = Assert.Single(
            _harness.StateStore.Records,
            record => record.EventsProcessed > 0);
        Assert.True(intermediate.EventsProcessed < 700);
        Assert.Contains(
            _harness.Hosts,
            host => host.ProjectorName == projectorName && host.AppliedEventIds.Count > 0);
        Assert.True((await grain.GetHealthStatusAsync()).IsHealthy == false);
    }

    [Fact]
    public async Task Known_present_host_restore_throw_retires_durably()
    {
        const string projectorName = "g38-restore-throw";
        await _harness.SeedKnownPresentCheckpointAsync(projectorName, oldSafeVersion: 700, BuildEvents(20));
        _harness.StreamBehavior = StateStreamBehavior.Success;
        _harness.RestoreBehavior = RestoreBehavior.Throw;

        var grain = _cluster.Client.GetGrain<IMultiProjectionGrain>(projectorName);
        await grain.GetStatusAsync();
        await WaitUntilAsync(
            () => _harness.ProviderStorage.CommittedStates.Any(IsRetiredState),
            TimeSpan.FromSeconds(10));
        await WaitUntilAsync(
            () => _harness.StateStore.Records.Any(record => record.EventsProcessed > 0),
            TimeSpan.FromSeconds(10));

        AssertRetiredCommittedState(projectorName);
        var intermediate = Assert.Single(
            _harness.StateStore.Records,
            record => record.EventsProcessed > 0);
        Assert.True(intermediate.EventsProcessed < 700);
        Assert.Contains(
            _harness.Hosts,
            host => host.ProjectorName == projectorName && host.AppliedEventIds.Count > 0);
        Assert.False((await grain.GetHealthStatusAsync()).IsHealthy);
    }

    [Fact]
    public async Task Pre_record_query_failure_does_not_retire_the_existing_watermark()
    {
        const string projectorName = "g38-pre-record-failure";
        await _harness.SeedKnownPresentCheckpointAsync(projectorName, oldSafeVersion: 700, BuildEvents(0));
        _harness.GetLatestBehavior = GetLatestBehavior.Throw;

        var grain = _cluster.Client.GetGrain<IMultiProjectionGrain>(projectorName);
        await grain.GetStatusAsync();
        await Task.Delay(300);

        var provider = _harness.ProviderStorage.Get(projectorName);
        Assert.NotNull(provider);
        Assert.Equal(700, provider!.LastGoodSafeVersion);
        Assert.Equal(701, provider.LastGoodPayloadBytes);
        Assert.Equal(702, provider.LastGoodOriginalSizeBytes);
        Assert.Equal(703, provider.LastGoodEventsProcessed);
        Assert.Empty(_harness.Hosts.Single(host => host.ProjectorName == projectorName).AppliedEventIds);
        Assert.False((await grain.GetHealthStatusAsync()).IsHealthy);
    }

    [Fact]
    public async Task Retirement_write_failure_rolls_back_and_does_not_start_rebuild_or_claim_checkpoint()
    {
        const string projectorName = "g38-retirement-write-failure";
        await _harness.SeedKnownPresentCheckpointAsync(projectorName, oldSafeVersion: 700, BuildEvents(20));
        _harness.RestoreBehavior = RestoreBehavior.ResultBoxFailure;
        _harness.ProviderStorage.FailWrites = true;

        var grain = _cluster.Client.GetGrain<IMultiProjectionGrain>(projectorName);
        await grain.GetStatusAsync();
        await WaitUntilAsync(
            () => _harness.ProviderStorage.WriteAttempts > 0,
            TimeSpan.FromSeconds(10));
        await Task.Delay(300);

        var host = Assert.Single(_harness.Hosts, candidate => candidate.ProjectorName == projectorName);
        Assert.Empty(host.AppliedEventIds);
        Assert.Equal(0, _harness.StateStore.UpsertCalls);
        Assert.Equal(700, _harness.ProviderStorage.Get(projectorName)!.LastGoodSafeVersion);
        Assert.Equal(701, _harness.ProviderStorage.Get(projectorName)!.LastGoodPayloadBytes);
        Assert.Equal(702, _harness.ProviderStorage.Get(projectorName)!.LastGoodOriginalSizeBytes);
        Assert.Equal(703, _harness.ProviderStorage.Get(projectorName)!.LastGoodEventsProcessed);

        var health = await grain.GetHealthStatusAsync();
        Assert.False(health.IsHealthy);
        Assert.Contains("retirement", health.LastError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            _harness.LoggerProvider.Messages,
            message => message.Contains("Integrity watermark retirement failed", StringComparison.Ordinal) &&
                       message.Contains("no durable checkpoint", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Retirement_write_then_deactivation_persists_zero_and_fresh_activation_consumes_it()
    {
        const string projectorName = "g38-crash-window";
        await _harness.SeedKnownPresentCheckpointAsync(projectorName, oldSafeVersion: 700, BuildEvents(20));
        _harness.RestoreBehavior = RestoreBehavior.ResultBoxFailure;
        _harness.EventStore.BlockReads = true;
        _harness.EventStore.ReturnEmptyAfterBlockedRead = true;

        var grain = _cluster.Client.GetGrain<IMultiProjectionGrain>(projectorName);
        await grain.GetStatusAsync();
        await WaitUntilAsync(
            () =>
            {
                var provider = _harness.ProviderStorage.Get(projectorName);
                return provider is not null &&
                       provider.LastGoodSafeVersion == 0 &&
                       provider.LastGoodPayloadBytes == 0 &&
                       provider.LastGoodOriginalSizeBytes == 0 &&
                       provider.LastGoodEventsProcessed == 0;
            },
            TimeSpan.FromSeconds(10));
        await _harness.EventStore.WaitForBlockedReadStartedAsync();

        // Stop at the retirement/rebuild boundary: the blocked authoritative read has not delivered an event yet.
        var deactivation = grain.RequestDeactivationAsync();
        _harness.EventStore.ReleaseBlockedRead();
        await deactivation;
        await WaitUntilAsync(
            () => _harness.StateStore.Records.Any(record => record.EventsProcessed == 0),
            TimeSpan.FromSeconds(10));

        var zeroCheckpoint = _harness.StateStore.Records.Last(record => record.EventsProcessed == 0);
        Assert.Equal(0, zeroCheckpoint.EventsProcessed);
        Assert.NotEqual(700, zeroCheckpoint.EventsProcessed);
        Assert.Empty(_harness.Hosts.First(host => host.ProjectorName == projectorName).AppliedEventIds);

        var hostsBeforeFreshActivation = _harness.Hosts.ToHashSet();
        _harness.RestoreBehavior = RestoreBehavior.None;
        _harness.EventStore.BlockReads = false;
        var fresh = _cluster.Client.GetGrain<IMultiProjectionGrain>(projectorName);
        await fresh.GetStatusAsync();
        await WaitUntilAsync(
            () => _harness.Hosts.Any(host => !hostsBeforeFreshActivation.Contains(host) && host.RestoreCalls > 0),
            TimeSpan.FromSeconds(10));

        var freshRestore = _harness.StateStore.OpenedRecords.Last(record => record.EventsProcessed == 0);
        Assert.Equal(zeroCheckpoint.EventsProcessed, freshRestore.EventsProcessed);
        Assert.Equal(zeroCheckpoint.LastSortableUniqueId, freshRestore.LastSortableUniqueId);
        Assert.Contains(
            _harness.EventStore.ReadSinceValues,
            since => since is null || string.IsNullOrEmpty(since));
    }

    [Fact]
    public async Task Absent_external_snapshot_keeps_zero_reset_and_existing_log_reason()
    {
        const string projectorName = "g38-absent-compatibility";
        await _harness.SeedKnownPresentCheckpointAsync(projectorName, oldSafeVersion: 700, BuildEvents(0));
        _harness.GetLatestBehavior = GetLatestBehavior.Absent;

        var grain = _cluster.Client.GetGrain<IMultiProjectionGrain>(projectorName);
        await grain.GetStatusAsync();
        await WaitUntilAsync(() => _harness.ProviderStorage.WriteCalls > 0, TimeSpan.FromSeconds(10));

        var provider = _harness.ProviderStorage.Get(projectorName);
        Assert.NotNull(provider);
        Assert.Equal(0, provider!.LastGoodSafeVersion);
        Assert.Equal(0, provider.LastGoodPayloadBytes);
        Assert.Equal(0, provider.LastGoodOriginalSizeBytes);
        Assert.Equal(0, provider.LastGoodEventsProcessed);
        Assert.Contains(
            _harness.LoggerProvider.Messages,
            message => message.Contains("Resetting integrity guard", StringComparison.Ordinal) &&
                       message.Contains("external snapshot is missing", StringComparison.Ordinal));
        Assert.DoesNotContain(
            _harness.LoggerProvider.Messages,
            message => message.Contains("Integrity watermark retired", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Normal_regression_still_uses_the_unchanged_guard_without_a_provider_write()
    {
        const string projectorName = "g38-normal-regression";
        await _harness.SeedKnownPresentCheckpointAsync(projectorName, oldSafeVersion: 700, BuildEvents(0));
        _harness.RestoreBehavior = RestoreBehavior.None;

        var grain = _cluster.Client.GetGrain<IMultiProjectionGrain>(projectorName);
        await grain.GetStatusAsync();
        var host = Assert.Single(_harness.Hosts, candidate => candidate.ProjectorName == projectorName);
        host.ForceSafeVersion(699);
        var writesBefore = _harness.ProviderStorage.WriteCalls;

        var result = await grain.PersistStateAsync();

        Assert.True(result.IsSuccess);
        Assert.False(result.GetValue());
        Assert.Equal(writesBefore, _harness.ProviderStorage.WriteCalls);
        var provider = _harness.ProviderStorage.Get(projectorName)!;
        Assert.Equal(700, provider.LastGoodSafeVersion);
        Assert.Equal(701, provider.LastGoodPayloadBytes);
        Assert.Equal(702, provider.LastGoodOriginalSizeBytes);
        Assert.Equal(703, provider.LastGoodEventsProcessed);
    }

    [Fact]
    public async Task Fresh_recovery_establishes_w2_and_the_guard_is_rearmed_for_a_later_regression()
    {
        const string projectorName = "g38-do-it-twice";
        await _harness.SeedKnownPresentCheckpointAsync(projectorName, oldSafeVersion: 700, BuildEvents(10));
        _harness.RestoreBehavior = RestoreBehavior.ResultBoxFailure;

        var grain = _cluster.Client.GetGrain<IMultiProjectionGrain>(projectorName);
        await grain.GetStatusAsync();
        await WaitUntilAsync(
            () => _harness.StateStore.Records.Any(record => record.EventsProcessed > 0),
            TimeSpan.FromSeconds(10));
        var w2 = _harness.StateStore.LatestRecord!.EventsProcessed;
        Assert.True(w2 > 0);

        var host = _harness.Hosts
            .Where(candidate => candidate.ProjectorName == projectorName)
            .OrderByDescending(candidate => candidate.AppliedEventIds.Count)
            .First();
        Assert.Equal(w2, _harness.ProviderStorage.Get(projectorName)!.LastGoodSafeVersion);
        host.ForceSafeVersion(Math.Max(0, (int)w2 - 1));
        var writesBefore = _harness.ProviderStorage.WriteCalls;
        var blocked = await grain.PersistStateAsync();
        Assert.True(blocked.IsSuccess);
        Assert.False(blocked.GetValue());
        Assert.Equal(writesBefore, _harness.ProviderStorage.WriteCalls);

        host.ForceSafeVersion((int)w2);
        var atWatermark = await grain.PersistStateAsync();
        Assert.True(atWatermark.IsSuccess);
        Assert.True(atWatermark.GetValue());
    }

    [Fact]
    public async Task No_public_or_serialized_surface_was_added_for_retirement()
    {
        var grainType = typeof(MultiProjectionGrain);
        Assert.Null(grainType.GetMethod("RetireIntegrityWatermarkAsync", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(grainType.GetProperty("RestoreRetirementFailed", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(MultiProjectionGrainState).GetProperty("RestoreRetirementFailed"));
        Assert.Null(typeof(GeneralMultiProjectionActorOptions).GetProperty("IntegrityWatermark"));
    }

    private async Task AssertResultBoxRetiresAllFourFieldsAsync(
        string projectorName,
        StateStreamBehavior streamBehavior,
        RestoreBehavior restoreBehavior)
    {
        await _harness.SeedKnownPresentCheckpointAsync(projectorName, oldSafeVersion: 700, BuildEvents(0));
        _harness.StreamBehavior = streamBehavior;
        _harness.RestoreBehavior = restoreBehavior;

        var grain = _cluster.Client.GetGrain<IMultiProjectionGrain>(projectorName);
        await grain.GetStatusAsync();
        await WaitUntilAsync(() => _harness.ProviderStorage.WriteCalls > 0, TimeSpan.FromSeconds(10));

        var provider = _harness.ProviderStorage.Get(projectorName);
        Assert.NotNull(provider);
        Assert.Equal(0, provider!.LastGoodSafeVersion);
        Assert.Equal(0, provider.LastGoodPayloadBytes);
        Assert.Equal(0, provider.LastGoodOriginalSizeBytes);
        Assert.Equal(0, provider.LastGoodEventsProcessed);
        AssertRetiredWatermark(projectorName);
        Assert.False((await grain.GetHealthStatusAsync()).IsHealthy);
    }

    private void AssertRetiredWatermark(string projectorName)
    {
        var provider = _harness.ProviderStorage.Get(projectorName);
        Assert.NotNull(provider);
        Assert.Equal(0, provider.LastGoodSafeVersion);
        Assert.Equal(0, provider.LastGoodPayloadBytes);
        Assert.Equal(0, provider.LastGoodOriginalSizeBytes);
        Assert.Equal(0, provider.LastGoodEventsProcessed);

        AssertRetiredCommittedState(projectorName);
    }

    private void AssertRetiredCommittedState(string projectorName)
    {
        var committed = Assert.Single(
            _harness.ProviderStorage.CommittedStates,
            state => string.Equals(state.ProjectorName, projectorName, StringComparison.Ordinal) &&
                    IsRetiredState(state));
        Assert.Equal(0, committed.LastGoodSafeVersion);
        Assert.Equal(0, committed.LastGoodPayloadBytes);
        Assert.Equal(0, committed.LastGoodOriginalSizeBytes);
        Assert.Equal(0, committed.LastGoodEventsProcessed);
    }

    private static bool IsRetiredState(MultiProjectionGrainState state) =>
        state.LastGoodSafeVersion == 0 &&
        state.LastGoodPayloadBytes == 0 &&
        state.LastGoodOriginalSizeBytes == 0 &&
        state.LastGoodEventsProcessed == 0;

    private static IReadOnlyList<SerializableEvent> BuildEvents(int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => new SerializableEvent(
                [1],
                SortableUniqueId.GetTickString(1_000 + index) + SortableUniqueId.GetIdString(Guid.NewGuid()),
                Guid.NewGuid(),
                new EventMetadata("g38", "test", "watermark"),
                [],
                "G38Event"))
            .ToArray();
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        Func<string>? diagnostic = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(condition(), "Timed out waiting for the production-path condition. " + diagnostic?.Invoke());
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton(_ => TestDomainTypes.Create());
                    services.AddSingleton<IEventStore>(ActiveHarness!.EventStore);
                    services.AddSingleton<IMultiProjectionStateStore>(ActiveHarness.StateStore);
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddLogging(builder => builder.AddProvider(ActiveHarness!.LoggerProvider));
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions
                    {
                        SafeWindowMs = 20_000,
                        PersistIntervalSeconds = 0,
                        SkipPersistWhenSafeCheckpointUnchanged = true
                    });
                    services.AddSekibanDcbNativeRuntime();
                    // The test factory must be registered after the native runtime's default factory so the real grain
                    // activation path uses the deterministic host below.
                    services.AddSingleton<IProjectionActorHostFactory>(ActiveHarness.HostFactory);
                    services.AddGrainStorage("OrleansStorage", (_, _) => ActiveHarness!.ProviderStorage);
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    private sealed class FixedPortAllocator(int baseSiloPort, int baseGatewayPort) : ITestClusterPortAllocator
    {
        public (int, int) AllocateConsecutivePortPairs(int numPorts) => (baseSiloPort, baseGatewayPort);

        public void Dispose() { }
    }

    private enum RestoreBehavior
    {
        None,
        ResultBoxFailure,
        Throw
    }

    public enum StateStreamBehavior
    {
        Success,
        ResultBoxFailure,
        Throw,
        CanSeekThrows,
        LengthThrows,
        PositionThrows,
        ReadThrows,
        DisposeThrows
    }

    private enum GetLatestBehavior
    {
        Success,
        Absent,
        Throw
    }

    private sealed record SeededCheckpoint(int OldSafeVersion, string OldPosition);

    private sealed class WatermarkHarness
    {
        public WatermarkEventStore EventStore { get; } = new();
        public WatermarkStateStore StateStore { get; } = new();
        public WatermarkGrainStorage ProviderStorage { get; } = new();
        public WatermarkHostFactory HostFactory { get; }
        public ConcurrentBag<WatermarkProjectionHost> Hosts { get; } = new();
        public RecordingLoggerProvider LoggerProvider { get; } = new();
        public RestoreBehavior RestoreBehavior { get; set; }
        public StateStreamBehavior StreamBehavior
        {
            get => StateStore.StreamBehavior;
            set => StateStore.StreamBehavior = value;
        }

        public GetLatestBehavior GetLatestBehavior
        {
            get => StateStore.GetLatestBehavior;
            set => StateStore.GetLatestBehavior = value;
        }

        public WatermarkHarness()
        {
            HostFactory = new WatermarkHostFactory(this);
        }

        public void Reset()
        {
            EventStore.Reset();
            StateStore.Reset();
            ProviderStorage.Reset();
            while (Hosts.TryTake(out _)) { }
            LoggerProvider.Clear();
            RestoreBehavior = RestoreBehavior.None;
            StreamBehavior = StateStreamBehavior.Success;
            GetLatestBehavior = GetLatestBehavior.Success;
        }

        public async Task<SeededCheckpoint> SeedKnownPresentCheckpointAsync(
            string projectorName,
            int oldSafeVersion,
            IReadOnlyList<SerializableEvent> events)
        {
            EventStore.SetEvents(events);
            var oldPosition = SortableUniqueId.GetTickString(10_000) + SortableUniqueId.GetIdString(Guid.Empty);
            var request = new MultiProjectionStateWriteRequest(
                projectorName,
                "v1",
                typeof(SerializableMultiProjectionStateEnvelope).FullName!,
                oldPosition,
                oldSafeVersion,
                false,
                null,
                null,
                100,
                100,
                SortableUniqueId.GetTickString(9_000) + SortableUniqueId.GetIdString(Guid.Empty),
                DateTime.UtcNow,
                DateTime.UtcNow,
                "G38_TEST_SEED",
                "test");
            await StateStore.SeedAsync(request, [1]);
            ProviderStorage.Seed(
                projectorName,
                new MultiProjectionGrainState
                {
                    ProjectorName = projectorName,
                    ProjectorVersion = "v1",
                    LastSortableUniqueId = oldPosition,
                    EventsProcessed = oldSafeVersion,
                    LastGoodSafeVersion = oldSafeVersion,
                    LastGoodPayloadBytes = 701,
                    LastGoodOriginalSizeBytes = 702,
                    LastGoodEventsProcessed = 703
                });
            return new SeededCheckpoint(oldSafeVersion, oldPosition);
        }
    }

    private sealed class WatermarkHostFactory(WatermarkHarness harness) : IProjectionActorHostFactory
    {
        public IProjectionActorHost Create(
            string projectorName,
            GeneralMultiProjectionActorOptions? options = null,
            ILogger? logger = null)
        {
            var host = new WatermarkProjectionHost(harness, projectorName);
            harness.Hosts.Add(host);
            return host;
        }
    }

    private sealed class WatermarkProjectionHost(WatermarkHarness harness, string projectorName) : IProjectionActorHost
    {
        private readonly List<Guid> _appliedEventIds = [];
        private string? _safePosition;
        private int _forcedSafeVersion = -1;

        public string ProjectorName { get; } = projectorName;
        public int RestoreCalls { get; private set; }
        public int CompactionCalls { get; private set; }
        public List<Guid> AppliedEventIds => _appliedEventIds;
        public HashSet<Guid> NonIncrementalRetention { get; } = [];

        public void ForceSafeVersion(int version) => _forcedSafeVersion = version;

        public Task AddSerializableEventsAsync(IReadOnlyList<SerializableEvent> events, bool finishedCatchUp = true)
        {
            _appliedEventIds.AddRange(events.Select(item => item.Id));
            foreach (var item in events)
            {
                NonIncrementalRetention.Add(item.Id);
            }

            if (events.Count > 0)
            {
                _safePosition = events[^1].SortableUniqueIdValue;
            }

            return Task.CompletedTask;
        }

        public Task<ResultBox<ProjectionStateMetadata>> GetStateMetadataAsync(bool includeUnsafe = true)
        {
            var safeVersion = _forcedSafeVersion >= 0 ? _forcedSafeVersion : _appliedEventIds.Count;
            return Task.FromResult(ResultBox.FromValue(new ProjectionStateMetadata(
                ProjectorName,
                "v1",
                true,
                0,
                null,
                null,
                safeVersion,
                _safePosition)));
        }

        public Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true) =>
            Task.FromResult(ResultBox.Error<MultiProjectionState>(new InvalidOperationException("test host state unavailable")));

        public Task<ProjectionHeadStatus> GetProjectionHeadStatusAsync() => throw new NotSupportedException();

        public Task<ResultBox<bool>> WriteSnapshotToStreamAsync(
            Stream target,
            bool canGetUnsafeState,
            CancellationToken cancellationToken) => WriteSnapshotAsync(target, cancellationToken);

        public Task<ResultBox<bool>> WriteSnapshotForPersistenceToStreamAsync(
            Stream target,
            bool canGetUnsafeState,
            int offloadThresholdBytes,
            CancellationToken cancellationToken) => WriteSnapshotAsync(target, cancellationToken);

        private async Task<ResultBox<bool>> WriteSnapshotAsync(Stream target, CancellationToken cancellationToken)
        {
            await target.WriteAsync(new byte[] { 1 }, cancellationToken);
            return ResultBox.FromValue(true);
        }

        public async Task<ResultBox<bool>> RestoreSnapshotFromStreamAsync(Stream source, CancellationToken cancellationToken)
        {
            RestoreCalls++;
            if (harness.RestoreBehavior == RestoreBehavior.ResultBoxFailure)
            {
                return ResultBox.Error<bool>(new InvalidOperationException("injected host restore ResultBox failure"));
            }

            if (harness.RestoreBehavior == RestoreBehavior.Throw)
            {
                throw new InvalidOperationException("injected host restore throw");
            }

            var buffer = new byte[16];
            while (await source.ReadAsync(buffer, cancellationToken) > 0) { }
            return ResultBox.FromValue(true);
        }

        public Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
            SerializableQueryParameter query,
            int? safeVersion,
            string? safeThreshold,
            DateTime? safeThresholdTime,
            int? unsafeVersion) => throw new NotSupportedException();

        public Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(
            SerializableQueryParameter query,
            int? safeVersion,
            string? safeThreshold,
            DateTime? safeThresholdTime,
            int? unsafeVersion) => throw new NotSupportedException();

        public void ForcePromoteBufferedEvents() { }

        public void CompactSafeHistory()
        {
            CompactionCalls++;
            NonIncrementalRetention.Clear();
        }

        public void ForcePromoteAllBufferedEvents() { }

        public Task<string> GetSafeLastSortableUniqueIdAsync() => Task.FromResult(_safePosition ?? string.Empty);

        public Task<bool> IsSortableUniqueIdReceivedAsync(string sortableUniqueId) => Task.FromResult(false);

        public long EstimateStateSizeBytes(bool includeUnsafeDetails) => 1;

        public string PeekCurrentSafeWindowThreshold() =>
            SortableUniqueId.GetTickString(20_000) + SortableUniqueId.GetIdString(Guid.Empty);

        public string GetProjectorVersion() => "v1";

        public Task<ResultBox<bool>> RewriteSnapshotVersionAsync(
            Stream source,
            Stream target,
            string newVersion,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class WatermarkEventStore : IEventStore
    {
        private readonly object _gate = new();
        private IReadOnlyList<SerializableEvent> _events = [];
        private TaskCompletionSource<bool> _blockedReadStarted = CreateSignal();
        private TaskCompletionSource<bool> _releaseBlockedRead = CreateSignal();
        public List<string?> ReadSinceValues { get; } = [];
        public bool BlockReads { get; set; }
        public bool ReturnEmptyAfterBlockedRead { get; set; }

        private static TaskCompletionSource<bool> CreateSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Reset()
        {
            lock (_gate)
            {
                _events = [];
                ReadSinceValues.Clear();
                _blockedReadStarted = CreateSignal();
                _releaseBlockedRead = CreateSignal();
            }

            BlockReads = false;
            ReturnEmptyAfterBlockedRead = false;
        }

        public void SetEvents(IReadOnlyList<SerializableEvent> events)
        {
            lock (_gate)
            {
                _events = events;
                ReadSinceValues.Clear();
            }
        }

        public Task WaitForBlockedReadStartedAsync() => _blockedReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void ReleaseBlockedRead() => _releaseBlockedRead.TrySetResult(true);

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync()
        {
            lock (_gate)
            {
                return Task.FromResult(ResultBox.FromValue(_events.LastOrDefault()?.SortableUniqueIdValue ?? string.Empty));
            }
        }

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            ReadAllSerializableEventsAsync(since, null);

        public async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount)
        {
            IReadOnlyList<SerializableEvent> events;
            Task? releaseTask = null;
            bool returnEmpty = false;
            lock (_gate)
            {
                ReadSinceValues.Add(since?.Value);
                events = _events.ToArray();
                if (BlockReads)
                {
                    _blockedReadStarted.TrySetResult(true);
                    releaseTask = _releaseBlockedRead.Task;
                    returnEmpty = ReturnEmptyAfterBlockedRead;
                }
            }

            if (releaseTask is not null)
            {
                await releaseTask;
                if (returnEmpty)
                {
                    return ResultBox.FromValue<IEnumerable<SerializableEvent>>([]);
                }
            }

            var result = events.Where(item => since is null ||
                string.Compare(item.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
            if (maxCount.HasValue)
            {
                result = result.Take(maxCount.Value);
            }

            return ResultBox.FromValue<IEnumerable<SerializableEvent>>(result.ToArray());
        }

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => throw new NotSupportedException();
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => throw new NotSupportedException();
    }

    private sealed class WatermarkStateStore : IMultiProjectionStateStore
    {
        private readonly InMemoryMultiProjectionStateStore _inner = new();
        private readonly object _gate = new();
        private int _upsertCalls;
        public int UpsertCalls => Volatile.Read(ref _upsertCalls);
        public List<MultiProjectionStateRecord> Records { get; } = [];
        public List<MultiProjectionStateRecord> OpenedRecords { get; } = [];
        public MultiProjectionStateRecord? LatestRecord { get; private set; }
        public GetLatestBehavior GetLatestBehavior { get; set; }
        public StateStreamBehavior StreamBehavior { get; set; }

        public void Reset()
        {
            _inner.Clear();
            lock (_gate)
            {
                Volatile.Write(ref _upsertCalls, 0);
                Records.Clear();
                OpenedRecords.Clear();
                LatestRecord = null;
            }
            GetLatestBehavior = GetLatestBehavior.Success;
            StreamBehavior = StateStreamBehavior.Success;
        }

        public async Task SeedAsync(MultiProjectionStateWriteRequest request, byte[] payload)
        {
            var result = await _inner.UpsertFromStreamAsync(request, new MemoryStream(payload), payload.Length);
            Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        }

        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
            string projectorName,
            string projectorVersion,
            CancellationToken cancellationToken = default)
        {
            if (GetLatestBehavior == GetLatestBehavior.Throw)
            {
                throw new InvalidOperationException("injected GetLatestForVersionAsync throw");
            }

            if (GetLatestBehavior == GetLatestBehavior.Absent)
            {
                return Task.FromResult(
                    ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty));
            }

            return _inner.GetLatestForVersionAsync(projectorName, projectorVersion, cancellationToken);
        }

        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(
            string projectorName,
            CancellationToken cancellationToken = default) => _inner.GetLatestAnyVersionAsync(projectorName, cancellationToken);

        public Task<ResultBox<bool>> UpsertAsync(
            MultiProjectionStateRecord record,
            int offloadThresholdBytes = 1_000_000,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _upsertCalls);
            lock (_gate)
            {
                Records.Add(record);
                LatestRecord = record;
            }
            return _inner.UpsertAsync(record, offloadThresholdBytes, cancellationToken);
        }

        public Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(CancellationToken cancellationToken = default) =>
            _inner.ListAllAsync(cancellationToken);

        public Task<ResultBox<bool>> DeleteAsync(
            string projectorName,
            string projectorVersion,
            CancellationToken cancellationToken = default) => _inner.DeleteAsync(projectorName, projectorVersion, cancellationToken);

        public Task<ResultBox<int>> DeleteAllAsync(string? projectorName = null, CancellationToken cancellationToken = default) =>
            _inner.DeleteAllAsync(projectorName, cancellationToken);

        public Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(
            MultiProjectionStateRecord record,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                OpenedRecords.Add(record);
            }

            switch (StreamBehavior)
            {
                case StateStreamBehavior.ResultBoxFailure:
                    return Task.FromResult(ResultBox.Error<Stream>(new InvalidOperationException("injected stream-open ResultBox failure")));
                case StateStreamBehavior.Throw:
                    throw new InvalidOperationException("injected stream-open throw");
                case StateStreamBehavior.CanSeekThrows:
                case StateStreamBehavior.LengthThrows:
                case StateStreamBehavior.PositionThrows:
                case StateStreamBehavior.ReadThrows:
                case StateStreamBehavior.DisposeThrows:
                    return Task.FromResult(ResultBox.FromValue<Stream>(new ThrowingReadStream(StreamBehavior)));
                default:
                    return _inner.OpenStateDataReadStreamAsync(record, cancellationToken);
            }
        }

        public Task<ResultBox<bool>> UpsertFromStreamAsync(
            MultiProjectionStateWriteRequest request,
            Stream stream,
            int offloadThresholdBytes,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _upsertCalls);
            var record = request.ToRecord();
            lock (_gate)
            {
                Records.Add(record);
                LatestRecord = record;
            }
            return _inner.UpsertFromStreamAsync(request, stream, offloadThresholdBytes, cancellationToken);
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);

        public void Clear()
        {
            while (Messages.TryDequeue(out _)) { }
        }

        public void Dispose() { }
    }

    private sealed class RecordingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            messages.Enqueue(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose() { }
        }
    }

    private sealed class ThrowingReadStream(StateStreamBehavior behavior) : MemoryStream([1])
    {
        public override bool CanSeek => behavior == StateStreamBehavior.CanSeekThrows
            ? throw new InvalidOperationException("injected stream CanSeek throw")
            : base.CanSeek;

        public override long Length => behavior == StateStreamBehavior.LengthThrows
            ? throw new InvalidOperationException("injected stream Length throw")
            : base.Length;

        public override long Position
        {
            get => base.Position;
            set
            {
                if (behavior == StateStreamBehavior.PositionThrows)
                {
                    throw new InvalidOperationException("injected stream Position throw");
                }

                base.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            behavior == StateStreamBehavior.ReadThrows
                ? throw new InvalidOperationException("injected stream Read throw")
                : base.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            behavior == StateStreamBehavior.ReadThrows
                ? ValueTask.FromException<int>(new InvalidOperationException("injected stream ReadAsync throw"))
                : base.ReadAsync(buffer, cancellationToken);

        public override ValueTask DisposeAsync() =>
            behavior == StateStreamBehavior.DisposeThrows
                ? ValueTask.FromException(new InvalidOperationException("injected stream DisposeAsync throw"))
                : base.DisposeAsync();
    }

    private sealed class WatermarkGrainStorage : IGrainStorage
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, MultiProjectionGrainState> _states = new(StringComparer.Ordinal);
        private int _writeCalls;
        private int _writeAttempts;
        public bool FailWrites { get; set; }
        public int WriteCalls => Volatile.Read(ref _writeCalls);
        public int WriteAttempts => Volatile.Read(ref _writeAttempts);
        public List<MultiProjectionGrainState> CommittedStates { get; } = [];

        public void Reset()
        {
            lock (_gate)
            {
                _states.Clear();
                CommittedStates.Clear();
                Volatile.Write(ref _writeCalls, 0);
                Volatile.Write(ref _writeAttempts, 0);
            }
            FailWrites = false;
        }

        public void Seed(string grainKey, MultiProjectionGrainState state)
        {
            lock (_gate)
            {
                _states[grainKey] = state.Clone();
            }
        }

        public MultiProjectionGrainState? Get(string grainKey)
        {
            lock (_gate)
            {
                var pair = _states
                    .Where(item =>
                    string.Equals(item.Key, grainKey, StringComparison.Ordinal) ||
                    item.Key.EndsWith("/" + grainKey, StringComparison.Ordinal) ||
                    item.Key.EndsWith("@" + grainKey, StringComparison.Ordinal))
                    .OrderByDescending(item => item.Key.Length)
                    .FirstOrDefault();
                return pair.Value?.Clone();
            }
        }

        public Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (_gate)
            {
                var key = grainId.ToString();
                var pair = _states.FirstOrDefault(item =>
                    string.Equals(item.Key, key, StringComparison.Ordinal) ||
                    key.EndsWith("/" + item.Key, StringComparison.Ordinal) ||
                    key.EndsWith("@" + item.Key, StringComparison.Ordinal));
                if (pair.Value is { } state && grainState.State is MultiProjectionGrainState)
                {
                    grainState.State = (T)(object)state.Clone();
                    grainState.RecordExists = true;
                }
                else
                {
                    grainState.RecordExists = false;
                }
            }

            return Task.CompletedTask;
        }

        public Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            Interlocked.Increment(ref _writeAttempts);
            if (FailWrites)
            {
                throw new InvalidOperationException("injected Orleans retirement write failure");
            }

            if (grainState.State is MultiProjectionGrainState state)
            {
                lock (_gate)
                {
                    _states[grainId.ToString()] = state.Clone();
                    CommittedStates.Add(state.Clone());
                    Interlocked.Increment(ref _writeCalls);
                }
            }

            return Task.CompletedTask;
        }

        public Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (_gate)
            {
                _states.Remove(grainId.ToString());
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TestDomainTypes
    {
        public static DcbDomainTypes Create()
        {
            var eventTypes = new SimpleEventTypes();
            eventTypes.RegisterEventType<G38Event>("G38Event");
            var multiProjectors = new SimpleMultiProjectorTypes();
            multiProjectors.RegisterProjector<G38Projector>();
            return new DcbDomainTypes(
                eventTypes,
                new SimpleTagTypes(),
                new SimpleTagProjectorTypes(),
                new SimpleTagStatePayloadTypes(),
                multiProjectors,
                new SimpleQueryTypes(),
                new System.Text.Json.JsonSerializerOptions());
        }
    }

    public record G38Event(string Value) : IEventPayload;

    [GenerateSerializer]
    public record G38Projector : IMultiProjector<G38Projector>
    {
        [Id(0)]
        public int Count { get; init; }
        public static string MultiProjectorName => "g38-unused-projector";
        public static string MultiProjectorVersion => "v1";
        public static G38Projector GenerateInitialPayload() => new();
        public static ResultBox<G38Projector> Project(
            G38Projector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload with { Count = payload.Count + 1 });
    }
}

[CollectionDefinition("watermark-retirement", DisableParallelization = true)]
public sealed class WatermarkRetirementCollection : ICollectionFixture<object> { }
