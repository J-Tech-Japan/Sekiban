using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.SortableUniqueIdWait.Tests;

/// <summary>
///     These tests enter through the production Orleans facade and its DI composition, then use a counting Orleans
///     grain contract proxy as the deterministic boundary. The proxy returns genuine serialized query results, so the
///     facade's request serialization, wait policy, grain calls, response deserialization, and ResultBox boundary all
///     remain under test without a real sleep or an internal helper call.
/// </summary>
public sealed class OrleansFacadeProxyAcceptanceTests
{
    [Fact]
    public async Task StrictSingleTimeout_IsTypedBeforeSerializationOrGrainQueryExecution_AndRecordsMetrics()
    {
        var clock = NewClock();
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-6), Guid.Empty);
        ProductionFacadeAcceptanceTests.AcceptanceQuery.ResetObservations();
        var observation = new GrainObservation { ProbeResult = false, LastObservedPosition = "observed-position" };
        using var scope = CreateExecutor(clock, observation);
        using var metrics = new WaitMetricCapture();

        var result = await scope.Executor.QueryAsync<ProductionFacadeAcceptanceTests.AcceptanceResult>(
            new ProductionFacadeAcceptanceTests.AcceptanceQuery { WaitForSortableUniqueId = target });

        Assert.False(result.IsSuccess);
        var timeout = Assert.IsType<SortableUniqueIdWaitTimeoutException>(result.GetException());
        Assert.Equal(target, timeout.TargetSortableUniqueId);
        Assert.Equal(TimeSpan.FromSeconds(5), timeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(5), timeout.Elapsed);
        Assert.Equal("observed-position", timeout.LastObservedSortableUniqueId);
        Assert.Equal(25, observation.ProbeCalls);
        Assert.Equal(1, observation.HeadStatusCalls);
        Assert.Equal(0, observation.ExecuteQueryCalls);
        Assert.Equal(0, Volatile.Read(ref ProductionFacadeAcceptanceTests.AcceptanceQuery.HandleCalls));
        Assert.Equal(0, Volatile.Read(ref ProductionFacadeAcceptanceTests.AcceptanceQuery.SerializationWrites));
        Assert.Contains(
            metrics.Histograms,
            value => value.Surface == "orleans_with_result_single" && value.Mode == "strict" && value.Outcome == "timeout");
        Assert.Contains(
            metrics.Counters,
            value => value.Surface == "orleans_with_result_single" && value.Mode == "strict" && value.Outcome == "timeout");
    }

    [Fact]
    public async Task StrictListTimeout_IsTypedBeforeListGrainQueryExecution()
    {
        var clock = NewClock();
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-6), Guid.Empty);
        ProductionFacadeAcceptanceTests.AcceptanceListQuery.ResetObservations();
        var observation = new GrainObservation { ProbeResult = false, LastObservedPosition = "observed-position" };
        using var scope = CreateExecutor(clock, observation);
        using var metrics = new WaitMetricCapture();

        var result = await scope.Executor.QueryAsync<ProductionFacadeAcceptanceTests.AcceptanceItem>(
            new ProductionFacadeAcceptanceTests.AcceptanceListQuery
            {
                WaitForSortableUniqueId = target,
                PageNumber = 1,
                PageSize = 10
            });

        Assert.False(result.IsSuccess);
        var timeout = Assert.IsType<SortableUniqueIdWaitTimeoutException>(result.GetException());
        Assert.Equal("observed-position", timeout.LastObservedSortableUniqueId);
        Assert.Equal(25, observation.ProbeCalls);
        Assert.Equal(1, observation.HeadStatusCalls);
        Assert.Equal(0, observation.ExecuteListQueryCalls);
        Assert.Equal(0, Volatile.Read(ref ProductionFacadeAcceptanceTests.AcceptanceListQuery.HandleCalls));
        Assert.Equal(0, Volatile.Read(ref ProductionFacadeAcceptanceTests.AcceptanceListQuery.SerializationWrites));
        Assert.Contains(
            metrics.Histograms,
            value => value.Surface == "orleans_with_result_list" &&
                value.Mode == "strict" && value.Outcome == "timeout");
    }

    [Fact]
    public async Task LegacySingleTimeout_RemainsFailOpenAndExecutesTheRealFacadeGrainCall()
    {
        var clock = NewClock();
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-6), Guid.Empty);
        ProductionFacadeAcceptanceTests.LegacyAcceptanceQuery.ResetObservations();
        var observation = new GrainObservation { ProbeResult = false };
        using var scope = CreateExecutor(clock, observation);

        var result = await scope.Executor.QueryAsync<ProductionFacadeAcceptanceTests.AcceptanceResult>(
            new ProductionFacadeAcceptanceTests.LegacyAcceptanceQuery { WaitForSortableUniqueId = target });

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(25, observation.ProbeCalls);
        Assert.Equal(0, observation.HeadStatusCalls);
        Assert.Equal(1, observation.ExecuteQueryCalls);
        Assert.True(Volatile.Read(ref ProductionFacadeAcceptanceTests.LegacyAcceptanceQuery.SerializationWrites) >= 1);
        Assert.Equal(TimeSpan.FromSeconds(5), clock.Elapsed);
        Assert.Contains(
            scope.Metrics.Histograms,
            value => value.Surface == "orleans_with_result_single" &&
                value.Mode == "legacy" && value.Outcome == "timeout");
    }

    [Fact]
    public async Task LegacyListTimeout_RemainsFailOpenAndExecutesTheRealListGrainCall()
    {
        var clock = NewClock();
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-6), Guid.Empty);
        ProductionFacadeAcceptanceTests.LegacyAcceptanceListQuery.ResetObservations();
        var observation = new GrainObservation { ProbeResult = false };
        using var scope = CreateExecutor(clock, observation);

        var result = await scope.Executor.QueryAsync<ProductionFacadeAcceptanceTests.AcceptanceItem>(
            new ProductionFacadeAcceptanceTests.LegacyAcceptanceListQuery
            {
                WaitForSortableUniqueId = target,
                PageNumber = 1,
                PageSize = 10
            });

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(25, observation.ProbeCalls);
        Assert.Equal(0, observation.HeadStatusCalls);
        Assert.Equal(1, observation.ExecuteListQueryCalls);
        Assert.True(Volatile.Read(ref ProductionFacadeAcceptanceTests.LegacyAcceptanceListQuery.SerializationWrites) >= 1);
        Assert.Equal(TimeSpan.FromSeconds(5), clock.Elapsed);
        Assert.Contains(
            scope.Metrics.Histograms,
            value => value.Surface == "orleans_with_result_list" &&
                value.Mode == "legacy" && value.Outcome == "timeout");
    }

    [Fact]
    public async Task StrictArrival_UsesGrainSuccessAndExecutesQueryOnce()
    {
        var clock = NewClock();
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime, Guid.Empty);
        ProductionFacadeAcceptanceTests.AcceptanceQuery.ResetObservations();
        var observation = new GrainObservation { ProbeResult = true };
        using var scope = CreateExecutor(clock, observation);

        var result = await scope.Executor.QueryAsync<ProductionFacadeAcceptanceTests.AcceptanceResult>(
            new ProductionFacadeAcceptanceTests.AcceptanceQuery { WaitForSortableUniqueId = target });

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(7, result.GetValue().Count);
        Assert.Equal(1, observation.ProbeCalls);
        Assert.Equal(1, observation.ExecuteQueryCalls);
        Assert.True(Volatile.Read(ref ProductionFacadeAcceptanceTests.AcceptanceQuery.SerializationWrites) >= 1);
        Assert.Contains(
            scope.Metrics.Histograms,
            value => value.Surface == "orleans_with_result_single" && value.Mode == "strict" && value.Outcome == "arrived");
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
    }

    [Fact]
    public async Task LegacyArrival_UsesGrainSuccessAndExecutesQueryOnce()
    {
        var clock = NewClock();
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime, Guid.Empty);
        ProductionFacadeAcceptanceTests.LegacyAcceptanceQuery.ResetObservations();
        var observation = new GrainObservation { ProbeResult = true };
        using var scope = CreateExecutor(clock, observation);

        var result = await scope.Executor.QueryAsync<ProductionFacadeAcceptanceTests.AcceptanceResult>(
            new ProductionFacadeAcceptanceTests.LegacyAcceptanceQuery { WaitForSortableUniqueId = target });

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(1, observation.ProbeCalls);
        Assert.Equal(1, observation.ExecuteQueryCalls);
        Assert.True(Volatile.Read(ref ProductionFacadeAcceptanceTests.LegacyAcceptanceQuery.SerializationWrites) >= 1);
        Assert.Contains(
            scope.Metrics.Histograms,
            value => value.Surface == "orleans_with_result_single" &&
                value.Mode == "legacy" && value.Outcome == "arrived");
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
    }

    private static FakeClock NewClock() =>
        new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static ExecutorScope CreateExecutor(FakeClock clock, GrainObservation observation)
    {
        var client = DispatchProxy.Create<IClusterClient, ClusterClientProxy>();
        var grain = DispatchProxy.Create<IMultiProjectionGrain, GrainProxy>();
        ClusterClientProxy.Current.Value = grain;
        GrainProxy.Current.Value = new GrainContext(observation, ProductionFacadeAcceptanceTests.CreateDomain());

        var generator = new MonotonicSortableUniqueIdGenerator(clock);
        var policy = new SortableUniqueIdWaitPolicy(
            clock,
            (delay, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                clock.Advance(delay);
                return Task.CompletedTask;
            });
        var services = new ServiceCollection();
        services.AddSingleton<IClusterClient>(client);
        services.AddSingleton<IEventStore>(new EmptyEventStore());
        services.AddSingleton(ProductionFacadeAcceptanceTests.CreateDomain());
        services.AddSingleton(policy);
        services.AddSingleton<ISekibanExecutor>(serviceProvider => new OrleansDcbExecutor(
            serviceProvider.GetRequiredService<IClusterClient>(),
            serviceProvider.GetRequiredService<IEventStore>(),
            serviceProvider.GetRequiredService<DcbDomainTypes>(),
            null,
            null,
            null,
            generator,
            new SortableUniqueIdSeedCoordinator(generator),
            serviceProvider.GetRequiredService<SortableUniqueIdWaitPolicy>()));

        var provider = services.BuildServiceProvider();
        return new ExecutorScope(provider, provider.GetRequiredService<ISekibanExecutor>(), observation, clock);
    }

    private sealed class ExecutorScope : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly GrainObservation _observation;
        private readonly FakeClock _clock;

        public ExecutorScope(ServiceProvider provider, ISekibanExecutor executor, GrainObservation observation, FakeClock clock)
        {
            _provider = provider;
            Executor = executor;
            _observation = observation;
            _clock = clock;
            Metrics = new WaitMetricCapture();
        }

        public ISekibanExecutor Executor { get; }
        public WaitMetricCapture Metrics { get; }

        public void Dispose()
        {
            Metrics.Dispose();
            _provider.Dispose();
            ClusterClientProxy.Current.Value = null;
            GrainProxy.Current.Value = null;
        }
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        private long _timestamp;

        public DateTimeOffset UtcNow => _utcNow;
        public TimeSpan Elapsed => TimeSpan.FromTicks(_timestamp);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan amount)
        {
            _utcNow += amount;
            _timestamp += amount.Ticks;
        }
    }

    private sealed class GrainObservation
    {
        public bool ProbeResult { get; init; }
        public string? LastObservedPosition { get; init; }
        public int ProbeCalls;
        public int HeadStatusCalls;
        public int ExecuteQueryCalls;
        public int ExecuteListQueryCalls;
    }

    private sealed record GrainContext(GrainObservation Observation, DcbDomainTypes Domain);

    private class ClusterClientProxy : DispatchProxy
    {
        public static AsyncLocal<IMultiProjectionGrain?> Current { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.IsGenericMethod == true && targetMethod.Name == "GetGrain" &&
                targetMethod.GetGenericArguments()[0] == typeof(IMultiProjectionGrain))
            {
                return Current.Value ?? throw new InvalidOperationException("No fake grain is configured.");
            }

            throw new InvalidOperationException($"Unexpected IClusterClient call: {targetMethod?.Name}");
        }
    }

    private class GrainProxy : DispatchProxy
    {
        public static AsyncLocal<GrainContext?> Current { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var context = Current.Value ?? throw new InvalidOperationException("No fake grain context is configured.");
            var observation = context.Observation;
            switch (targetMethod?.Name)
            {
                case nameof(IMultiProjectionGrain.IsSortableUniqueIdReceived):
                    Interlocked.Increment(ref observation.ProbeCalls);
                    return Task.FromResult(observation.ProbeResult);
                case nameof(IMultiProjectionGrain.GetProjectionHeadStatusAsync):
                    Interlocked.Increment(ref observation.HeadStatusCalls);
                    return Task.FromResult(new MultiProjectionHeadStatusSnapshot(
                        ProductionFacadeAcceptanceTests.AcceptanceProjector.MultiProjectorName,
                        "1.0.0",
                        0,
                        context.Observation.LastObservedPosition,
                        0,
                        null,
                        false,
                        null,
                        null,
                        0));
                case nameof(IMultiProjectionGrain.ExecuteQueryAsync):
                    Interlocked.Increment(ref observation.ExecuteQueryCalls);
                    return CreateQueryResultAsync(
                        args?[0] as SerializableQueryParameter ??
                        throw new InvalidOperationException("Missing serialized query parameter."),
                        context.Domain);
                case nameof(IMultiProjectionGrain.ExecuteListQueryAsync):
                    Interlocked.Increment(ref observation.ExecuteListQueryCalls);
                    return CreateListQueryResultAsync(
                        args?[0] as SerializableQueryParameter ??
                        throw new InvalidOperationException("Missing serialized list query parameter."),
                        context.Domain);
                default:
                    throw new InvalidOperationException($"Unexpected grain call: {targetMethod?.Name}");
            }
        }

        private static async Task<SerializableQueryResult> CreateQueryResultAsync(
            SerializableQueryParameter parameter,
            DcbDomainTypes domain)
        {
            var query = (IQueryCommon)(await parameter.ToQueryAsync(domain)).GetValue();
            var result = new QueryResultGeneral(
                new ProductionFacadeAcceptanceTests.AcceptanceResult(7),
                typeof(ProductionFacadeAcceptanceTests.AcceptanceResult).AssemblyQualifiedName!,
                query);
            return await SerializableQueryResult.CreateFromAsync(result, domain.JsonSerializerOptions);
        }

        private static async Task<SerializableListQueryResult> CreateListQueryResultAsync(
            SerializableQueryParameter parameter,
            DcbDomainTypes domain)
        {
            var query = (IListQueryCommon)(await parameter.ToQueryAsync(domain)).GetValue();
            var result = new ListQueryResultGeneral(
                1,
                1,
                1,
                10,
                [new ProductionFacadeAcceptanceTests.AcceptanceItem(7)],
                typeof(ProductionFacadeAcceptanceTests.AcceptanceItem).AssemblyQualifiedName!,
                query);
            return await SerializableListQueryResult.CreateFromAsync(result, domain.JsonSerializerOptions);
        }
    }

    private sealed class WaitMetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        public List<WaitObservation> Counters { get; } = [];
        public List<WaitObservation> Histograms { get; } = [];

        public WaitMetricCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == SortableUniqueIdWaitTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
                Counters.Add(WaitObservation.From(instrument, tags)));
            _listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
                Histograms.Add(WaitObservation.From(instrument, tags)));
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed record WaitObservation(string Surface, string Mode, string Outcome)
    {
        public static WaitObservation From(
            Instrument instrument,
            ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var surface = "";
            var mode = "";
            var outcome = "";
            foreach (var tag in tags)
            {
                var value = tag.Value?.ToString() ?? "";
                switch (tag.Key)
                {
                    case "surface": surface = value; break;
                    case "mode": mode = value; break;
                    case "outcome": outcome = value; break;
                }
            }

            return new(surface, mode, outcome);
        }
    }

    private sealed class EmptyEventStore : IEventStore
    {
        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) =>
            Task.FromResult(ResultBox.FromValue<IEnumerable<TagStream>>([]));
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) =>
            Task.FromResult(ResultBox.Error<TagState>(new NotSupportedException()));
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) =>
            Task.FromResult(ResultBox.FromValue(false));
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue(0L));
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) =>
            Task.FromResult(ResultBox.FromValue<IEnumerable<TagInfo>>([]));
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue<IEnumerable<SerializableEvent>>([]));
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) =>
            Task.FromResult(ResultBox.FromValue<IEnumerable<SerializableEvent>>([]));
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) =>
            Task.FromResult(ResultBox.Error<SerializableEvent>(new NotSupportedException()));
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue<IEnumerable<SerializableEvent>>([]));
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
            IEnumerable<SerializableEvent> events) =>
            Task.FromResult(ResultBox.Error<(IReadOnlyList<SerializableEvent>, IReadOnlyList<TagWriteResult>)>(new NotSupportedException()));
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() =>
            Task.FromResult(ResultBox.FromValue(""));
    }
}
