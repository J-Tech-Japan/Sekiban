using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;

namespace Sekiban.Dcb.SortableUniqueIdWait.WithoutResult.Tests;

/// <summary>
///     Exception-based Orleans facade acceptance tests. The grain contract is a deterministic counting proxy, while
///     the production executor, shared wait policy, serialized query boundary, and exception boundary are real.
/// </summary>
public sealed class OrleansWithoutResultFacadeAcceptanceTests
{
    [Fact]
    public void Architecture_AllWithoutResultEntrypointsBindTheSharedPolicy()
    {
        Assert.Contains(
            typeof(CoreGeneralSekibanExecutor).GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => field.FieldType == typeof(SortableUniqueIdWaitPolicy));
        Assert.Contains(
            typeof(Sekiban.Dcb.Orleans.OrleansDcbExecutor).GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => field.FieldType == typeof(SortableUniqueIdWaitPolicy));
    }

    [Fact]
    public async Task StrictSingleTimeout_IsTypedBeforeSerializationOrGrainQueryExecution()
    {
        var clock = NewClock();
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-6), Guid.Empty);
        WithoutResultQuery.ResetObservations();
        var observation = new GrainObservation { LastObservedPosition = "observed-position" };
        using var scope = CreateExecutor(clock, observation);
        using var metrics = new WaitMetricCapture();

        var exception = await Assert.ThrowsAsync<SortableUniqueIdWaitTimeoutException>(() =>
            scope.Executor.QueryAsync<WithoutResultValue>(new WithoutResultQuery { WaitForSortableUniqueId = target }));

        Assert.Equal(target, exception.TargetSortableUniqueId);
        Assert.Equal("observed-position", exception.LastObservedSortableUniqueId);
        Assert.Equal(TimeSpan.FromSeconds(5), exception.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(5), exception.Elapsed);
        Assert.Equal(25, observation.ProbeCalls);
        Assert.Equal(1, observation.HeadStatusCalls);
        Assert.Equal(0, observation.ExecuteQueryCalls);
        Assert.Equal(0, WithoutResultQuery.SerializationWrites);
        Assert.Contains(
            metrics.Histograms,
            value => value.Surface == "orleans_without_result_single" &&
                value.Mode == "strict" && value.Outcome == "timeout");
    }

    [Fact]
    public async Task StrictListTimeout_IsTypedBeforeListGrainQueryExecution()
    {
        var clock = NewClock();
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-6), Guid.Empty);
        WithoutResultListQuery.ResetObservations();
        var observation = new GrainObservation { LastObservedPosition = "observed-position" };
        using var scope = CreateExecutor(clock, observation);
        using var metrics = new WaitMetricCapture();

        var exception = await Assert.ThrowsAsync<SortableUniqueIdWaitTimeoutException>(() =>
            scope.Executor.QueryAsync<WithoutResultItem>(
                new WithoutResultListQuery
                {
                    WaitForSortableUniqueId = target,
                    PageNumber = 1,
                    PageSize = 10
                }));

        Assert.Equal(target, exception.TargetSortableUniqueId);
        Assert.Equal("observed-position", exception.LastObservedSortableUniqueId);
        Assert.Equal(25, observation.ProbeCalls);
        Assert.Equal(1, observation.HeadStatusCalls);
        Assert.Equal(0, observation.ExecuteListQueryCalls);
        Assert.Equal(0, WithoutResultListQuery.SerializationWrites);
        Assert.Contains(
            metrics.Histograms,
            value => value.Surface == "orleans_without_result_list" &&
                value.Mode == "strict" && value.Outcome == "timeout");
    }

    [Fact]
    public async Task LegacySingleTimeout_RemainsFailOpenAndExecutesTheGrainQuery()
    {
        var clock = NewClock();
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-6), Guid.Empty);
        LegacyWithoutResultQuery.ResetObservations();
        var observation = new GrainObservation();
        using var scope = CreateExecutor(clock, observation);

        var result = await scope.Executor.QueryAsync<WithoutResultValue>(
            new LegacyWithoutResultQuery { WaitForSortableUniqueId = target });

        Assert.Equal(9, result.Count);
        Assert.Equal(25, observation.ProbeCalls);
        Assert.Equal(1, observation.ExecuteQueryCalls);
        Assert.True(LegacyWithoutResultQuery.SerializationWrites >= 1);
        Assert.Equal(TimeSpan.FromSeconds(5), clock.Elapsed);
        Assert.Contains(
            scope.Metrics.Histograms,
            value => value.Surface == "orleans_without_result_single" &&
                value.Mode == "legacy" && value.Outcome == "timeout");
    }

    [Fact]
    public async Task LegacyListTimeout_RemainsFailOpenAndExecutesTheListGrainQuery()
    {
        var clock = NewClock();
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-6), Guid.Empty);
        LegacyWithoutResultListQuery.ResetObservations();
        var observation = new GrainObservation();
        using var scope = CreateExecutor(clock, observation);

        var result = await scope.Executor.QueryAsync<WithoutResultItem>(
            new LegacyWithoutResultListQuery
            {
                WaitForSortableUniqueId = target,
                PageNumber = 1,
                PageSize = 10
            });

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(25, observation.ProbeCalls);
        Assert.Equal(0, observation.HeadStatusCalls);
        Assert.Equal(1, observation.ExecuteListQueryCalls);
        Assert.True(LegacyWithoutResultListQuery.SerializationWrites >= 1);
        Assert.Equal(TimeSpan.FromSeconds(5), clock.Elapsed);
        Assert.Contains(
            scope.Metrics.Histograms,
            value => value.Surface == "orleans_without_result_list" &&
                value.Mode == "legacy" && value.Outcome == "timeout");
    }

    [Fact]
    public async Task StrictArrival_UsesGrainSuccessAndExecutesQueryOnce()
    {
        var clock = NewClock();
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime, Guid.Empty);
        WithoutResultQuery.ResetObservations();
        var observation = new GrainObservation { ProbeResult = true };
        using var scope = CreateExecutor(clock, observation);

        var result = await scope.Executor.QueryAsync<WithoutResultValue>(
            new WithoutResultQuery { WaitForSortableUniqueId = target });

        Assert.Equal(9, result.Count);
        Assert.Equal(1, observation.ProbeCalls);
        Assert.Equal(1, observation.ExecuteQueryCalls);
        Assert.True(WithoutResultQuery.SerializationWrites >= 1);
        Assert.Contains(
            scope.Metrics.Histograms,
            value => value.Surface == "orleans_without_result_single" &&
                value.Mode == "strict" && value.Outcome == "arrived");
    }

    private static FakeClock NewClock() =>
        new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static ExecutorScope CreateExecutor(FakeClock clock, GrainObservation observation)
    {
        var client = DispatchProxy.Create<IClusterClient, ClusterClientProxy>();
        var grain = DispatchProxy.Create<IMultiProjectionGrain, GrainProxy>();
        ClusterClientProxy.Current.Value = grain;
        GrainProxy.Current.Value = new GrainContext(observation, CreateDomain());

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
        services.AddSingleton<IEventStore>(new InMemoryEventStore());
        services.AddSingleton(CreateDomain());
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
        return new ExecutorScope(provider, provider.GetRequiredService<ISekibanExecutor>());
    }

    private static DcbDomainTypes CreateDomain() =>
        DcbDomainTypesExtensions.Simple(types =>
        {
            types.MultiProjectorTypes.RegisterProjector<WithoutResultProjector>();
            types.QueryTypes.RegisterQuery<WithoutResultQuery>();
            types.QueryTypes.RegisterQuery<LegacyWithoutResultQuery>();
            types.QueryTypes.RegisterListQuery<WithoutResultListQuery>();
            types.QueryTypes.RegisterListQuery<LegacyWithoutResultListQuery>();
        });

    private sealed class ExecutorScope : IDisposable
    {
        private readonly ServiceProvider _provider;
        public ExecutorScope(ServiceProvider provider, ISekibanExecutor executor)
        {
            _provider = provider;
            Executor = executor;
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
                return Current.Value ?? throw new InvalidOperationException("No fake grain configured.");
            }

            throw new InvalidOperationException($"Unexpected IClusterClient call: {targetMethod?.Name}");
        }
    }

    private class GrainProxy : DispatchProxy
    {
        public static AsyncLocal<GrainContext?> Current { get; } = new();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var context = Current.Value ?? throw new InvalidOperationException("No fake grain context configured.");
            switch (targetMethod?.Name)
            {
                case nameof(IMultiProjectionGrain.IsSortableUniqueIdReceived):
                    Interlocked.Increment(ref context.Observation.ProbeCalls);
                    return Task.FromResult(context.Observation.ProbeResult);
                case nameof(IMultiProjectionGrain.GetProjectionHeadStatusAsync):
                    Interlocked.Increment(ref context.Observation.HeadStatusCalls);
                    return Task.FromResult(new MultiProjectionHeadStatusSnapshot(
                        WithoutResultProjector.MultiProjectorName,
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
                    Interlocked.Increment(ref context.Observation.ExecuteQueryCalls);
                    return CreateQueryResultAsync(
                        args?[0] as SerializableQueryParameter ??
                        throw new InvalidOperationException("Missing serialized query parameter."),
                        context.Domain);
                case nameof(IMultiProjectionGrain.ExecuteListQueryAsync):
                    Interlocked.Increment(ref context.Observation.ExecuteListQueryCalls);
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
            return await SerializableQueryResult.CreateFromAsync(
                new QueryResultGeneral(
                    new WithoutResultValue(9),
                    typeof(WithoutResultValue).AssemblyQualifiedName!,
                    query),
                domain.JsonSerializerOptions);
        }

        private static async Task<SerializableListQueryResult> CreateListQueryResultAsync(
            SerializableQueryParameter parameter,
            DcbDomainTypes domain)
        {
            var query = (IListQueryCommon)(await parameter.ToQueryAsync(domain)).GetValue();
            return await SerializableListQueryResult.CreateFromAsync(
                new ListQueryResultGeneral(
                    1,
                    1,
                    1,
                    10,
                    [new WithoutResultItem(9)],
                    typeof(WithoutResultItem).AssemblyQualifiedName!,
                    query),
                domain.JsonSerializerOptions);
        }
    }

    private sealed class WaitMetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
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

    public sealed record WithoutResultProjector : IMultiProjector<WithoutResultProjector>
    {
        public int Count { get; init; }
        public static string MultiProjectorName => "sortable-unique-id-wait-without-result";
        public static string MultiProjectorVersion => "1.0.0";
        public static WithoutResultProjector GenerateInitialPayload() => new();
        public static WithoutResultProjector Project(
            WithoutResultProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => payload with { Count = payload.Count + 1 };
    }

    public sealed record WithoutResultQuery : IMultiProjectionQuery<WithoutResultProjector, WithoutResultQuery, WithoutResultValue>,
        IStrictWaitForSortableUniqueId
    {
        public static int SerializationWrites;
        public string? WaitForSortableUniqueId { get; init; }
        [JsonConverter(typeof(WithoutResultQueryProbeConverter))]
        public string Probe { get; init; } = "serialized";
        public static WithoutResultValue HandleQuery(WithoutResultProjector projector, WithoutResultQuery query, IQueryContext context) =>
            new(projector.Count);
        public static void ResetObservations() => SerializationWrites = 0;
    }

    public sealed record LegacyWithoutResultQuery : IMultiProjectionQuery<WithoutResultProjector, LegacyWithoutResultQuery, WithoutResultValue>,
        IWaitForSortableUniqueId
    {
        public static int SerializationWrites;
        public string? WaitForSortableUniqueId { get; init; }
        [JsonConverter(typeof(LegacyWithoutResultQueryProbeConverter))]
        public string Probe { get; init; } = "serialized";
        public static WithoutResultValue HandleQuery(WithoutResultProjector projector, LegacyWithoutResultQuery query, IQueryContext context) =>
            new(projector.Count);
        public static void ResetObservations() => SerializationWrites = 0;
    }

    public sealed record WithoutResultListQuery : IMultiProjectionListQuery<WithoutResultProjector, WithoutResultListQuery, WithoutResultItem>,
        IStrictWaitForSortableUniqueId,
        IQueryPagingParameter
    {
        public static int SerializationWrites;
        public string? WaitForSortableUniqueId { get; init; }
        public int? PageNumber { get; init; }
        public int? PageSize { get; init; }
        [JsonConverter(typeof(WithoutResultListQueryProbeConverter))]
        public string Probe { get; init; } = "serialized";
        public static IEnumerable<WithoutResultItem> HandleFilter(WithoutResultProjector projector, WithoutResultListQuery query, IQueryContext context) =>
            [new(projector.Count)];
        public static IEnumerable<WithoutResultItem> HandleSort(IEnumerable<WithoutResultItem> items, WithoutResultListQuery query, IQueryContext context) => items;
        public static void ResetObservations() => SerializationWrites = 0;
    }

    public sealed record LegacyWithoutResultListQuery :
        IMultiProjectionListQuery<WithoutResultProjector, LegacyWithoutResultListQuery, WithoutResultItem>,
        IWaitForSortableUniqueId,
        IQueryPagingParameter
    {
        public static int SerializationWrites;
        public string? WaitForSortableUniqueId { get; init; }
        public int? PageNumber { get; init; }
        public int? PageSize { get; init; }
        [JsonConverter(typeof(LegacyWithoutResultListQueryProbeConverter))]
        public string Probe { get; init; } = "serialized";
        public static IEnumerable<WithoutResultItem> HandleFilter(
            WithoutResultProjector projector,
            LegacyWithoutResultListQuery query,
            IQueryContext context) => [new(projector.Count)];
        public static IEnumerable<WithoutResultItem> HandleSort(
            IEnumerable<WithoutResultItem> items,
            LegacyWithoutResultListQuery query,
            IQueryContext context) => items;
        public static void ResetObservations() => SerializationWrites = 0;
    }

    public sealed record WithoutResultValue(int Count);
    public sealed record WithoutResultItem(int Count);

    public sealed class WithoutResultQueryProbeConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString() ?? "";
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            Interlocked.Increment(ref WithoutResultQuery.SerializationWrites);
            writer.WriteStringValue(value);
        }
    }

    public sealed class LegacyWithoutResultQueryProbeConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString() ?? "";
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            Interlocked.Increment(ref LegacyWithoutResultQuery.SerializationWrites);
            writer.WriteStringValue(value);
        }
    }

    public sealed class WithoutResultListQueryProbeConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString() ?? "";
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            Interlocked.Increment(ref WithoutResultListQuery.SerializationWrites);
            writer.WriteStringValue(value);
        }
    }

    public sealed class LegacyWithoutResultListQueryProbeConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString() ?? "";
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            Interlocked.Increment(ref LegacyWithoutResultListQuery.SerializationWrites);
            writer.WriteStringValue(value);
        }
    }
}
