using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;

namespace Sekiban.Dcb.SortableUniqueIdWait.Tests;

/// <summary>
///     Acceptance tests deliberately enter through the WithResult GeneralSekibanExecutor facade. The strict cases
///     prove the timeout happens before query serialization/handling; the legacy case proves the historical in-memory
///     no-wait behavior remains fail-open. Every observation is captured from the production query path.
/// </summary>
public sealed class ProductionFacadeAcceptanceTests
{
    [Fact]
    public async Task StrictSingleTimeout_IsTypedBeforeSerializationOrQueryExecution_AndRecordsMetrics()
    {
        var clock = new FakeClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-6), Guid.Empty);
        var query = new AcceptanceQuery { WaitForSortableUniqueId = target };
        AcceptanceQuery.ResetObservations();
        LegacyAcceptanceQuery.ResetObservations();

        using var metrics = new WaitMetricCapture();
        var executor = CreateExecutor(clock, out _);

        var result = await executor.QueryAsync<AcceptanceResult>(query);

        Assert.False(result.IsSuccess);
        var timeout = Assert.IsType<SortableUniqueIdWaitTimeoutException>(result.GetException());
        Assert.Equal(target, timeout.TargetSortableUniqueId);
        Assert.Equal(TimeSpan.FromSeconds(5), timeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(5), timeout.Elapsed);
        Assert.Null(timeout.LastObservedSortableUniqueId);
        Assert.Equal(0, Volatile.Read(ref AcceptanceQuery.HandleCalls));
        Assert.Equal(0, Volatile.Read(ref AcceptanceQuery.SerializationWrites));

        Assert.Contains(
            metrics.Histograms,
            observation => observation.Surface == "in_memory_single" &&
                observation.Mode == "strict" &&
                observation.Outcome == "timeout");
        Assert.Contains(
            metrics.Counters,
            observation => observation.Surface == "in_memory_single" &&
                observation.Mode == "strict" &&
                observation.Outcome == "timeout");
        Assert.All(
            metrics.Histograms,
            observation => Assert.Equal(["surface", "mode", "outcome"], observation.TagKeys));
    }

    [Fact]
    public async Task StrictListTimeout_IsTypedBeforeListQueryExecution()
    {
        var clock = new FakeClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-6), Guid.Empty);
        AcceptanceListQuery.ResetObservations();
        var executor = CreateExecutor(clock, out _);
        using var metrics = new WaitMetricCapture();

        var result = await executor.QueryAsync<AcceptanceItem>(
            new AcceptanceListQuery
            {
                WaitForSortableUniqueId = target,
                PageNumber = 1,
                PageSize = 10
            });

        Assert.False(result.IsSuccess);
        var timeout = Assert.IsType<SortableUniqueIdWaitTimeoutException>(result.GetException());
        Assert.Equal(target, timeout.TargetSortableUniqueId);
        Assert.Equal(0, Volatile.Read(ref AcceptanceListQuery.HandleCalls));
        Assert.Equal(0, Volatile.Read(ref AcceptanceListQuery.SerializationWrites));
        Assert.Contains(
            metrics.Histograms,
            observation => observation.Surface == "in_memory_list" &&
                observation.Mode == "strict" && observation.Outcome == "timeout");
    }

    [Fact]
    public async Task LegacyInMemoryQuery_RemainsFailOpenWithoutWaiting()
    {
        var clock = new FakeClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-6), Guid.Empty);
        AcceptanceQuery.ResetObservations();
        LegacyAcceptanceQuery.ResetObservations();
        var executor = CreateExecutor(clock, out _);

        var result = await executor.QueryAsync<AcceptanceResult>(
            new LegacyAcceptanceQuery { WaitForSortableUniqueId = target });

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(1, Volatile.Read(ref LegacyAcceptanceQuery.HandleCalls));
        Assert.Equal(0, Volatile.Read(ref LegacyAcceptanceQuery.SerializationWrites));
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
    }

    [Fact]
    public async Task StrictArrival_TraversesActorCatchupAndExecutesQuery()
    {
        var clock = new FakeClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime, Guid.Empty);
        AcceptanceQuery.ResetObservations();
        var executor = CreateExecutor(clock, out var store);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new AcceptanceEvent("arrived"),
            CreateDomain().JsonSerializerOptions);
        var write = await store.WriteSerializableEventsAsync(
            [
                new SerializableEvent(
                    payload,
                    target,
                    Guid.NewGuid(),
                    new EventMetadata("event", "command", "test"),
                    [],
                    nameof(AcceptanceEvent))
            ]);
        Assert.True(write.IsSuccess, write.IsSuccess ? "" : write.GetException().ToString());

        var result = await executor.QueryAsync<AcceptanceResult>(
            new AcceptanceQuery { WaitForSortableUniqueId = target });

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(1, result.GetValue().Count);
        Assert.Equal(1, Volatile.Read(ref AcceptanceQuery.HandleCalls));
        Assert.Equal(0, Volatile.Read(ref AcceptanceQuery.SerializationWrites));
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
    }

    [Fact]
    public async Task StrictTimeout_RemainsTypedWhenBestEffortPublisherSwallowsAFoldFailure()
    {
        var clock = new FakeClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var target = SortableUniqueId.Generate(clock.UtcNow.UtcDateTime.AddSeconds(-6), Guid.Empty);
        PublisherFaultQuery.ResetObservations();
        var executor = CreateExecutor(clock, out _, out var accessor, out var publisher);

        var actorResult = await accessor.GetActorAsync<GeneralMultiProjectionActor>(
            PublisherFaultProjector.MultiProjectorName);
        Assert.True(actorResult.IsSuccess, actorResult.IsSuccess ? "" : actorResult.GetException().ToString());

        var poisonedEvent = new Event(
            new PublisherFaultEvent("poison"),
            target,
            nameof(PublisherFaultEvent),
            Guid.NewGuid(),
            new EventMetadata("event", "command", "test"),
            []);
        await publisher.PublishAsync([(poisonedEvent, (IReadOnlyCollection<ITag>)[])]);

        using var metrics = new WaitMetricCapture();
        var result = await executor.QueryAsync<PublisherFaultResult>(
            new PublisherFaultQuery { WaitForSortableUniqueId = target });

        Assert.False(result.IsSuccess);
        var timeout = Assert.IsType<SortableUniqueIdWaitTimeoutException>(result.GetException());
        Assert.Equal(target, timeout.TargetSortableUniqueId);
        Assert.Null(timeout.LastObservedSortableUniqueId);
        Assert.Equal(0, Volatile.Read(ref PublisherFaultQuery.HandleCalls));
        Assert.Contains(
            metrics.Histograms,
            observation => observation.Surface == "in_memory_single" &&
                observation.Mode == "strict" && observation.Outcome == "timeout");
    }

    private static GeneralSekibanExecutor CreateExecutor(
        FakeClock clock,
        out InMemoryEventStore store,
        out InMemoryObjectAccessor accessor,
        out InMemoryMultiProjectionEventPublisher publisher)
    {
        var domain = CreateDomain();
        store = new InMemoryEventStore(domain.EventTypes);
        accessor = new InMemoryObjectAccessor(store, domain);
        publisher = new InMemoryMultiProjectionEventPublisher(accessor);
        var generator = new MonotonicSortableUniqueIdGenerator(clock);
        var policy = new SortableUniqueIdWaitPolicy(
            clock,
            (delay, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                clock.Advance(delay);
                return Task.CompletedTask;
            });

        return new GeneralSekibanExecutor(
            store,
            accessor,
            domain,
            publisher,
            null,
            generator,
            new SortableUniqueIdSeedCoordinator(generator),
            new DefaultServiceIdProvider(),
            policy);
    }

    private static GeneralSekibanExecutor CreateExecutor(FakeClock clock, out InMemoryEventStore store)
    {
        return CreateExecutor(clock, out store, out _, out _);
    }

    internal static DcbDomainTypes CreateDomain() =>
        DcbDomainTypesExtensions.Simple(types =>
        {
            types.EventTypes.RegisterEventType<AcceptanceEvent>();
            types.MultiProjectorTypes.RegisterProjector<AcceptanceProjector>();
            types.MultiProjectorTypes.RegisterProjector<PublisherFaultProjector>();
            types.QueryTypes.RegisterQuery<AcceptanceQuery>();
            types.QueryTypes.RegisterQuery<LegacyAcceptanceQuery>();
            types.QueryTypes.RegisterListQuery<AcceptanceListQuery>();
            types.QueryTypes.RegisterListQuery<LegacyAcceptanceListQuery>();
            types.QueryTypes.RegisterQuery<PublisherFaultQuery>();
        });

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _utcNow;
        private long _timestamp;

        public FakeClock(DateTimeOffset utcNow) => _utcNow = utcNow;

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

    private sealed class WaitMetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        public ConcurrentQueue<WaitObservation> Counters { get; } = new();
        public ConcurrentQueue<WaitObservation> Histograms { get; } = new();

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
            {
                Counters.Enqueue(WaitObservation.From(instrument, tags));
            });
            _listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            {
                Histograms.Enqueue(WaitObservation.From(instrument, tags));
            });
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed record WaitObservation(
        string Name,
        string Surface,
        string Mode,
        string Outcome,
        IReadOnlyList<string> TagKeys)
    {
        public static WaitObservation From(
            Instrument instrument,
            ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var surface = "";
            var mode = "";
            var outcome = "";
            var tagKeys = new List<string>();
            foreach (var tag in tags)
            {
                tagKeys.Add(tag.Key);
                var value = tag.Value?.ToString() ?? "";
                switch (tag.Key)
                {
                    case "surface": surface = value; break;
                    case "mode": mode = value; break;
                    case "outcome": outcome = value; break;
                }
            }

            return new(instrument.Name, surface, mode, outcome, tagKeys);
        }
    }

    public sealed record AcceptanceEvent(string Value) : IEventPayload;

    public sealed record PublisherFaultEvent(string Value) : IEventPayload;

    public record AcceptanceProjector : IMultiProjector<AcceptanceProjector>
    {
        public int Count { get; init; }
        public static string MultiProjectorName => "sortable-unique-id-wait-acceptance";
        public static string MultiProjectorVersion => "1.0.0";
        public static AcceptanceProjector GenerateInitialPayload() => new();

        public static ResultBox<AcceptanceProjector> Project(
            AcceptanceProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) =>
            ResultBox.FromValue(payload with { Count = payload.Count + 1 });
    }

    public sealed record PublisherFaultProjector : IMultiProjector<PublisherFaultProjector>
    {
        public static string MultiProjectorName => "sortable-unique-id-wait-publisher-fault";
        public static string MultiProjectorVersion => "1.0.0";
        public static PublisherFaultProjector GenerateInitialPayload() => new();

        public static ResultBox<PublisherFaultProjector> Project(
            PublisherFaultProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) =>
            throw new InvalidOperationException("publisher fold failure");
    }

    public sealed record AcceptanceQuery : IMultiProjectionQuery<AcceptanceProjector, AcceptanceQuery, AcceptanceResult>,
        IStrictWaitForSortableUniqueId
    {
        public static int HandleCalls;
        public static int SerializationWrites;

        public string? WaitForSortableUniqueId { get; init; }

        [JsonConverter(typeof(AcceptanceQueryProbeConverter))]
        public string SerializationProbe { get; init; } = "serialized";

        public static ResultBox<AcceptanceResult> HandleQuery(
            AcceptanceProjector projector,
            AcceptanceQuery query,
            IQueryContext context)
        {
            Interlocked.Increment(ref HandleCalls);
            return ResultBox.FromValue(new AcceptanceResult(projector.Count));
        }

        public static void ResetObservations()
        {
            HandleCalls = 0;
            SerializationWrites = 0;
        }
    }

    public sealed record PublisherFaultQuery :
        IMultiProjectionQuery<PublisherFaultProjector, PublisherFaultQuery, PublisherFaultResult>,
        IStrictWaitForSortableUniqueId
    {
        public static int HandleCalls;
        public string? WaitForSortableUniqueId { get; init; }

        public static ResultBox<PublisherFaultResult> HandleQuery(
            PublisherFaultProjector projector,
            PublisherFaultQuery query,
            IQueryContext context)
        {
            Interlocked.Increment(ref HandleCalls);
            return ResultBox.FromValue(new PublisherFaultResult());
        }

        public static void ResetObservations() => HandleCalls = 0;
    }

    public sealed record LegacyAcceptanceQuery : IMultiProjectionQuery<AcceptanceProjector, LegacyAcceptanceQuery, AcceptanceResult>,
        IWaitForSortableUniqueId
    {
        public static int HandleCalls;
        public static int SerializationWrites;

        public string? WaitForSortableUniqueId { get; init; }

        [JsonConverter(typeof(LegacyAcceptanceQueryProbeConverter))]
        public string SerializationProbe { get; init; } = "serialized";

        public static ResultBox<AcceptanceResult> HandleQuery(
            AcceptanceProjector projector,
            LegacyAcceptanceQuery query,
            IQueryContext context)
        {
            Interlocked.Increment(ref HandleCalls);
            return ResultBox.FromValue(new AcceptanceResult(projector.Count));
        }

        public static void ResetObservations()
        {
            HandleCalls = 0;
            SerializationWrites = 0;
        }
    }

    public sealed record AcceptanceListQuery : IMultiProjectionListQuery<AcceptanceProjector, AcceptanceListQuery, AcceptanceItem>,
        IStrictWaitForSortableUniqueId,
        IQueryPagingParameter
    {
        public static int HandleCalls;
        public static int SerializationWrites;

        public string? WaitForSortableUniqueId { get; init; }
        public int? PageNumber { get; init; }
        public int? PageSize { get; init; }

        [JsonConverter(typeof(AcceptanceListQueryProbeConverter))]
        public string SerializationProbe { get; init; } = "serialized";

        public static ResultBox<IEnumerable<AcceptanceItem>> HandleFilter(
            AcceptanceProjector projector,
            AcceptanceListQuery query,
            IQueryContext context)
        {
            Interlocked.Increment(ref HandleCalls);
            return ResultBox.FromValue(Enumerable.Repeat(new AcceptanceItem(projector.Count), 1));
        }

        public static ResultBox<IEnumerable<AcceptanceItem>> HandleSort(
            IEnumerable<AcceptanceItem> filteredItems,
            AcceptanceListQuery query,
            IQueryContext context) => ResultBox.FromValue(filteredItems);

        public static void ResetObservations()
        {
            HandleCalls = 0;
            SerializationWrites = 0;
        }
    }

    public sealed record LegacyAcceptanceListQuery :
        IMultiProjectionListQuery<AcceptanceProjector, LegacyAcceptanceListQuery, AcceptanceItem>,
        IWaitForSortableUniqueId,
        IQueryPagingParameter
    {
        public static int HandleCalls;
        public static int SerializationWrites;

        public string? WaitForSortableUniqueId { get; init; }
        public int? PageNumber { get; init; }
        public int? PageSize { get; init; }

        [JsonConverter(typeof(LegacyAcceptanceListQueryProbeConverter))]
        public string SerializationProbe { get; init; } = "serialized";

        public static ResultBox<IEnumerable<AcceptanceItem>> HandleFilter(
            AcceptanceProjector projector,
            LegacyAcceptanceListQuery query,
            IQueryContext context)
        {
            Interlocked.Increment(ref HandleCalls);
            return ResultBox.FromValue(Enumerable.Repeat(new AcceptanceItem(projector.Count), 1));
        }

        public static ResultBox<IEnumerable<AcceptanceItem>> HandleSort(
            IEnumerable<AcceptanceItem> filteredItems,
            LegacyAcceptanceListQuery query,
            IQueryContext context) => ResultBox.FromValue(filteredItems);

        public static void ResetObservations()
        {
            HandleCalls = 0;
            SerializationWrites = 0;
        }
    }

    public sealed class AcceptanceQueryProbeConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString() ?? "";

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            Interlocked.Increment(ref AcceptanceQuery.SerializationWrites);
            writer.WriteStringValue(value);
        }
    }

    public sealed class LegacyAcceptanceQueryProbeConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString() ?? "";

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            Interlocked.Increment(ref LegacyAcceptanceQuery.SerializationWrites);
            writer.WriteStringValue(value);
        }
    }

    public sealed class AcceptanceListQueryProbeConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString() ?? "";

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            Interlocked.Increment(ref AcceptanceListQuery.SerializationWrites);
            writer.WriteStringValue(value);
        }
    }

    public sealed class LegacyAcceptanceListQueryProbeConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString() ?? "";

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            Interlocked.Increment(ref LegacyAcceptanceListQuery.SerializationWrites);
            writer.WriteStringValue(value);
        }
    }

    public sealed record AcceptanceResult(int Count);
    public sealed record AcceptanceItem(int Count);
    public sealed record PublisherFaultResult;
}
