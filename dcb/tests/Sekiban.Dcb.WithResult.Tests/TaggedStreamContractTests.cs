using System.Text;
using System.Text.Json;
using Dcb.Domain;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;
using CoreTagStateService = Sekiban.Dcb.Services.TagStateService;
using SqliteTagStateService = Sekiban.Dcb.Sqlite.Services.TagStateService;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Contract tests for SEK-G53's additive tagged callback stream. These target the provider boundary directly so a
///     list fallback cannot hide an exclusive/inclusive bound or cancellation regression.
/// </summary>
public class TaggedStreamContractTests
{
    private static readonly DateTime BaseTime = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string P0 = SortableUniqueId.Generate(BaseTime.AddSeconds(-1), Guid.Empty);
    private static readonly string P1 = SortableUniqueId.Generate(BaseTime, Guid.Empty);
    private static readonly string P2 = SortableUniqueId.Generate(BaseTime.AddSeconds(1), Guid.Empty);
    private static readonly string P3 = SortableUniqueId.Generate(BaseTime.AddSeconds(2), Guid.Empty);
    private static readonly string P4 = SortableUniqueId.Generate(BaseTime.AddSeconds(3), Guid.Empty);
    private static readonly DcbDomainTypes Domain = DomainType.GetDomainTypes();

    [Fact]
    public async Task InMemoryTaggedStream_SinceIsExclusive_UntilIsInclusive_AndOrdinallyOrdered()
    {
        var tag = new StreamTag("memory");
        var store = new CoreInMemoryEventStore(Domain.EventTypes);
        var write = await store.WriteSerializableEventsAsync(new[]
        {
            Event(P4, tag), Event(P2, tag), Event(P0, tag), Event(P3, tag), Event(P1, tag)
        });
        Assert.True(write.IsSuccess, write.IsSuccess ? string.Empty : write.GetException().ToString());

        var emitted = new List<string>();
        var result = await ((IStreamingTaggedSerializableEventStore)store).StreamSerializableEventsByTagAsync(
            tag,
            new SortableUniqueId(P1),
            new SortableUniqueId(P3),
            ev =>
            {
                emitted.Add(ev.SortableUniqueIdValue);
                return ValueTask.CompletedTask;
            });

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        Assert.Equal(new[] { P2, P3 }, emitted);
        Assert.Equal(2, result.GetValue().EventsRead);
        Assert.Equal(P3, result.GetValue().LastSortableUniqueId);
    }

    [Fact]
    public async Task SqliteTaggedStream_FivePointBoundsArePushedDownAndOrdinallyOrdered()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"g53-tagged-bounds-{Guid.NewGuid():N}.db");
        try
        {
            var tag = new StreamTag("sqlite-bounds");
            var store = new SqliteEventStore(databasePath, Domain.EventTypes);
            var write = await store.WriteSerializableEventsAsync(new[]
            {
                Event(P4, tag), Event(P2, tag), Event(P0, tag), Event(P3, tag), Event(P1, tag)
            });
            Assert.True(write.IsSuccess, write.IsSuccess ? string.Empty : write.GetException().ToString());

            var emitted = new List<string>();
            var result = await ((IStreamingTaggedSerializableEventStore)store).StreamSerializableEventsByTagAsync(
                tag,
                new SortableUniqueId(P1),
                new SortableUniqueId(P3),
                ev =>
                {
                    emitted.Add(ev.SortableUniqueIdValue);
                    return ValueTask.CompletedTask;
                });

            Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
            Assert.Equal(new[] { P2, P3 }, emitted);
            Assert.Equal(2, result.GetValue().EventsRead);
            Assert.Equal(P3, result.GetValue().LastSortableUniqueId);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task SqliteTaggedStream_CancellationAfterFirstCallbackStopsBeforeAnotherRow()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"g53-tagged-stream-{Guid.NewGuid():N}.db");
        try
        {
            var tag = new StreamTag("sqlite");
            var store = new SqliteEventStore(databasePath, Domain.EventTypes);
            var write = await store.WriteSerializableEventsAsync(new[] { Event(P1, tag), Event(P2, tag), Event(P3, tag) });
            Assert.True(write.IsSuccess, write.IsSuccess ? string.Empty : write.GetException().ToString());

            using var cancellation = new CancellationTokenSource();
            var callbacks = 0;
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await ((IStreamingTaggedSerializableEventStore)store).StreamSerializableEventsByTagAsync(
                    tag,
                    null,
                    null,
                    _ =>
                    {
                        callbacks++;
                        cancellation.Cancel();
                        return ValueTask.CompletedTask;
                    },
                    cancellation.Token));

            Assert.Equal(1, callbacks);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public void TaggedStreamResolver_RequiresBothTheInterfaceAndNativeDeclaration()
    {
        Assert.True(SekibanDcbCapabilityResolver.ResolveTaggedStream(new HonestStore(), "test").IsSupported);
        Assert.False(SekibanDcbCapabilityResolver.ResolveTaggedStream(new SilentStreamingStore(), "test").IsSupported);
        Assert.False(SekibanDcbCapabilityResolver.ResolveTaggedStream(new DeceptiveStore(), "test").IsSupported);
    }

    [Fact]
    public void BatchOnlyAccumulator_ReceivesExactlyOneElementThroughTheDefaultApplyEventMember()
    {
        using var concreteAccumulator = new BatchOnlyAccumulator();
        ITagStateProjectionAccumulator accumulator = concreteAccumulator;
        var @event = new SerializableEvent(
            new byte[] { 1 },
            P1,
            Guid.NewGuid(),
            new EventMetadata("cause", "correlation", "user"),
            new List<string>(),
            "Event");

        Assert.True(accumulator.ApplyEvent(@event, P1));
        Assert.Equal(1, concreteAccumulator.ApplyEventsCalls);
        Assert.Single(concreteAccumulator.LastEvents);
        Assert.Same(@event, concreteAccumulator.LastEvents[0]);
    }

    [Fact]
    public void Frozen_1018_accumulator_binary_loads_and_default_apply_event_delegates_once()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Sekiban.Dcb.TagStateAccumulator.Legacy1018Fixture.dll");
        var assembly = System.Reflection.Assembly.LoadFrom(fixturePath);
        var type = assembly.GetType(
            "Sekiban.Dcb.TagStateAccumulator.Legacy1018Fixture.Legacy1018TagStateAccumulator",
            throwOnError: true)!;
        using var concrete = Assert.IsAssignableFrom<IDisposable>(Activator.CreateInstance(type));
        var accumulator = Assert.IsAssignableFrom<ITagStateProjectionAccumulator>(concrete);
        var @event = new SerializableEvent(
            new byte[] { 1 },
            P1,
            Guid.NewGuid(),
            new EventMetadata("cause", "correlation", "user"),
            new List<string>(),
            "Event");

        Assert.True(accumulator.ApplyEvent(@event, P1));
        Assert.Equal(1, (int)type.GetProperty("ApplyEventsCalls")!.GetValue(concrete)!);
        Assert.Equal(1, (int)type.GetProperty("LastBatchCount")!.GetValue(concrete)!);
        Assert.Equal(P1, (string?)type.GetProperty("LastHead")!.GetValue(concrete));
    }

    [Fact]
    public async Task CoreAndSqliteTagStateServices_UseTheVerifiedStreamWithoutCallingTheListApi()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<ServiceCounterAdded>();
        var tagTypes = new SimpleTagTypes();
        var projectors = new SimpleTagProjectorTypes();
        projectors.RegisterProjector<ServiceCounterProjector>();
        var tag = new StreamTag("service");
        var @event = new SerializableEvent(
            JsonSerializer.SerializeToUtf8Bytes(new ServiceCounterAdded(3)),
            P1,
            Guid.NewGuid(),
            new EventMetadata("cause", "correlation", "user"),
            new List<string> { tag.GetTag() },
            nameof(ServiceCounterAdded));

        var coreStore = new StreamOnlyStore([@event]);
        var core = new CoreTagStateService(
            coreStore,
            eventTypes,
            tagTypes,
            projectors,
            new JsonSerializerOptions());
        var coreResult = await core.ProjectTagStateAsync(tag, ServiceCounterProjector.ProjectorName);

        Assert.True(coreResult.IsSuccess, coreResult.IsSuccess ? string.Empty : coreResult.GetException().ToString());
        Assert.Equal(3, Assert.IsType<ServiceCounterState>(coreResult.GetValue().State).Total);
        Assert.Equal(1, coreStore.StreamCalls);
        Assert.Equal(0, coreStore.ListCalls);

        var sqliteStore = new StreamOnlyStore([@event]);
        var sqlite = new SqliteTagStateService(
            sqliteStore,
            eventTypes,
            tagTypes,
            projectors,
            new JsonSerializerOptions());
        var sqliteResult = await sqlite.ProjectTagStateAsync(tag, ServiceCounterProjector.ProjectorName);

        Assert.True(sqliteResult.IsSuccess, sqliteResult.IsSuccess ? string.Empty : sqliteResult.GetException().ToString());
        Assert.Equal(3, Assert.IsType<ServiceCounterState>(sqliteResult.GetValue().State).Total);
        Assert.Equal(1, sqliteStore.StreamCalls);
        Assert.Equal(0, sqliteStore.ListCalls);
    }

    [Fact]
    public void DcbWorkflow_ObservesEveryTouchedProviderAndSetsTheControlledMemoryGate()
    {
        var assembly = typeof(TaggedStreamContractTests).Assembly;
        var resourceName = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith("run_test_dcb.yml", StringComparison.Ordinal));
        using var resource = assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(resource);
        using var reader = new StreamReader(resource!);
        var workflow = reader.ReadToEnd();
        foreach (var requiredPath in new[]
                 {
                     "dcb/src/Sekiban.Dcb.Core/**",
                     "dcb/src/Sekiban.Dcb.Orleans.Core/**",
                     "dcb/src/Sekiban.Dcb.Postgres/**",
                     "dcb/src/Sekiban.Dcb.Sqlite/**",
                     "dcb/src/Sekiban.Dcb.ColdStorage/**",
                     "dcb/tests/Sekiban.Dcb.WithResult.Tests/**",
                     "dcb/tests/Sekiban.Dcb.Orleans.Tests/**",
                     "dcb/tests/Sekiban.Dcb.Postgres.Tests/**",
                     "dcb/tests/Sekiban.Dcb.ColdEvents.Tests/**"
                 })
        {
            Assert.Contains(requiredPath, workflow, StringComparison.Ordinal);
        }

        Assert.Contains("SEKIBAN_STREAM_RESTORE_CONTROLLED_CEILING: '1'", workflow, StringComparison.Ordinal);
        Assert.Contains("SEKIBAN_TAG_STREAM_CONTROLLED_CEILING: '1'", workflow, StringComparison.Ordinal);
    }

    private static SerializableEvent Event(string sortableUniqueId, ITag tag) =>
        new(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new StudentCreated(Guid.NewGuid(), "student", 5),
                Domain.JsonSerializerOptions)),
            sortableUniqueId,
            Guid.NewGuid(),
            new EventMetadata("cause", "correlation", "user"),
            new List<string> { tag.GetTag() },
            nameof(StudentCreated));

    private sealed class StreamTag(string content) : ITag
    {
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => "Stream";
        public string GetTagContent() => content;
        public string GetTag() => $"Stream:{content}";
    }

    private class EmptyStore : IEventStore
    {
        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) =>
            throw new NotSupportedException();
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) => throw new NotSupportedException();
        public virtual Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) =>
            throw new NotSupportedException();
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => throw new NotSupportedException();
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => throw new NotSupportedException();
    }

    private sealed class HonestStore : EmptyStore, IStreamingTaggedSerializableEventStore, ITaggedStreamCapabilityProvider
    {
        public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
            TaggedStreamCapabilityDescriptor.Native("honest");

        public Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since,
            SortableUniqueId? until,
            Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResultBox.FromValue(new SerializableEventStreamReadResult(0, null)));
    }

    private sealed class SilentStreamingStore : EmptyStore, IStreamingTaggedSerializableEventStore
    {
        public Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since,
            SortableUniqueId? until,
            Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResultBox.FromValue(new SerializableEventStreamReadResult(0, null)));
    }

    private sealed class DeceptiveStore : EmptyStore, ITaggedStreamCapabilityProvider
    {
        public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
            TaggedStreamCapabilityDescriptor.Native("deceptive");
    }

    private sealed class StreamOnlyStore(IReadOnlyList<SerializableEvent> events) : EmptyStore,
        IStreamingTaggedSerializableEventStore, ITaggedStreamCapabilityProvider
    {
        private readonly IReadOnlyList<SerializableEvent> _events = events;
        public int StreamCalls { get; private set; }
        public int ListCalls { get; private set; }

        public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
            TaggedStreamCapabilityDescriptor.Native("service tripwire");

        public override Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null)
        {
            ListCalls++;
            throw new InvalidOperationException("The service streaming path must not call ReadSerializableEventsByTagAsync.");
        }

        public async Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since,
            SortableUniqueId? until,
            Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default)
        {
            StreamCalls++;
            var count = 0;
            string? lastId = null;
            foreach (var @event in _events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await onEvent(@event);
                count++;
                lastId = @event.SortableUniqueIdValue;
            }

            return ResultBox.FromValue(new SerializableEventStreamReadResult(count, lastId));
        }
    }

    private sealed record ServiceCounterAdded(int Delta) : IEventPayload;

    private sealed record ServiceCounterState(int Total) : ITagStatePayload;

    private sealed class ServiceCounterProjector : ITagProjector<ServiceCounterProjector>
    {
        public static string ProjectorVersion => "1";
        public static string ProjectorName => nameof(ServiceCounterProjector);

        public static ITagStatePayload Project(ITagStatePayload current, Event @event)
        {
            var state = current as ServiceCounterState ?? new ServiceCounterState(0);
            return @event.Payload is ServiceCounterAdded added
                ? state with { Total = state.Total + added.Delta }
                : state;
        }
    }

    private sealed class BatchOnlyAccumulator : ITagStateProjectionAccumulator
    {
        public int ApplyEventsCalls { get; private set; }
        public IReadOnlyList<SerializableEvent> LastEvents { get; private set; } = Array.Empty<SerializableEvent>();

        public bool ApplyState(SerializableTagState? cachedState) => true;

        public bool ApplyEvents(
            IReadOnlyList<SerializableEvent> events,
            string? latestSortableUniqueId,
            CancellationToken cancellationToken = default)
        {
            ApplyEventsCalls++;
            LastEvents = events;
            return true;
        }

        public SerializableTagState GetSerializedState() => new(
            Array.Empty<byte>(),
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            nameof(EmptyTagStatePayload),
            string.Empty,
            nameof(EmptyTagStatePayload));

        public void Dispose()
        {
        }
    }
}
