using System.Diagnostics;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Services;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Memory proof for SEK-G53. The supported source makes each serialized event only when the callback asks for it;
///     the paired list control deliberately retains every serialized payload before projection.
/// </summary>
public sealed class TaggedStreamMemoryTests
{
    private const int NormalEventCount = 24;
    private const int NormalPayloadBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    [Trait("Category", "TaggedStreamNormalMemory")]
    public async Task GeneratedTaggedHistory_FoldsOneEventAtATimeWithoutTheListApi()
    {
        var store = new GeneratedStreamingTagStore(NormalEventCount, NormalPayloadBytes);
        var result = await CreateService(store).ProjectTagStateAsync(MemoryTag.Instance, MemoryFoldProjector.ProjectorName);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        var state = Assert.IsType<MemoryFoldState>(result.GetValue().State);
        Assert.Equal(NormalEventCount, state.Applied);
        Assert.Equal(NormalPayloadBytes, state.LastPayloadLength);
        Assert.Equal(NormalEventCount, result.GetValue().EventCount);
        Assert.Equal(1, store.StreamCalls);
        Assert.Equal(0, store.ListCalls);
        Assert.InRange(store.MaxInFlightSerializedPayloadBytes, 1, NormalPayloadBytes + 1024);
    }

    [Fact]
    [Trait("Category", "TaggedStreamControlledMemory")]
    public async Task ControlledGeneratedHistory_StaysUnderTheStreamingAllocationCeiling_AndTheListControlRetainsTheHistory()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SEKIBAN_TAG_STREAM_CONTROLLED_CEILING"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        const int eventCount = 16;
        const int payloadBytes = 1_000_000;
        const long streamingAllocationCeilingBytes = 128L * 1024 * 1024;
        const long retainedStreamingPayloadCeilingBytes = 2L * 1024 * 1024;

        var streamingStore = new GeneratedStreamingTagStore(eventCount, payloadBytes);
        CollectForMeasurement();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var peakBefore = process.PeakWorkingSet64;

        var streamingResult = await CreateService(streamingStore)
            .ProjectTagStateAsync(MemoryTag.Instance, MemoryFoldProjector.ProjectorName);

        process.Refresh();
        var streamingAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var streamingPeakDeltaBytes = Math.Max(0, process.PeakWorkingSet64 - peakBefore);
        Console.WriteLine(
            $"SEK-G53 controlled tagged-stream telemetry: eventCount={eventCount}; payloadBytes={payloadBytes}; " +
            $"allocatedBytes={streamingAllocatedBytes}; peakDeltaBytes={streamingPeakDeltaBytes}; " +
            $"maxInFlightSerializedPayloadBytes={streamingStore.MaxInFlightSerializedPayloadBytes}; " +
            $"streamCalls={streamingStore.StreamCalls}; listCalls={streamingStore.ListCalls}");

        Assert.True(streamingResult.IsSuccess, streamingResult.IsSuccess ? string.Empty : streamingResult.GetException().ToString());
        Assert.Equal(eventCount, Assert.IsType<MemoryFoldState>(streamingResult.GetValue().State).Applied);
        Assert.InRange(streamingAllocatedBytes, 0, streamingAllocationCeilingBytes);
        Assert.InRange(streamingStore.MaxInFlightSerializedPayloadBytes, 1, retainedStreamingPayloadCeilingBytes);
        Assert.Equal(1, streamingStore.StreamCalls);
        Assert.Equal(0, streamingStore.ListCalls);

        // Same generated source characteristics, but without the optional capability. The service must take its frozen
        // list path, which deliberately retains the full serialized history; this is the control that kills a list
        // fallback mutation without depending on platform-specific working-set accounting.
        var bufferingControl = new GeneratedListTagStore(eventCount, payloadBytes);
        CollectForMeasurement();
        var bufferedAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var bufferedResult = await CreateService(bufferingControl)
            .ProjectTagStateAsync(MemoryTag.Instance, MemoryFoldProjector.ProjectorName);
        var bufferedAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - bufferedAllocatedBefore;
        Console.WriteLine(
            $"SEK-G53 controlled list-control telemetry: eventCount={eventCount}; payloadBytes={payloadBytes}; " +
            $"allocatedBytes={bufferedAllocatedBytes}; retainedSerializedPayloadBytes={bufferingControl.PeakListPayloadBytes}; " +
            $"listCalls={bufferingControl.ListCalls}");

        Assert.True(bufferedResult.IsSuccess, bufferedResult.IsSuccess ? string.Empty : bufferedResult.GetException().ToString());
        Assert.Equal(eventCount, Assert.IsType<MemoryFoldState>(bufferedResult.GetValue().State).Applied);
        Assert.Equal(1, bufferingControl.ListCalls);
        Assert.True(
            bufferingControl.PeakListPayloadBytes > retainedStreamingPayloadCeilingBytes,
            $"The deliberately buffering control retained only {bufferingControl.PeakListPayloadBytes} bytes.");
    }

    [Fact]
    [Trait("Category", "TaggedStreamManualMemory")]
    public async Task ManualIssueScaleTaggedHistory_ReportsPeakTelemetryWithoutMaterializingTheHistory()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SEKIBAN_TAG_STREAM_MANUAL_SMOKE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        // Same issue-scale shape as the production report: 1,001 payloads within the 1.1–1.4 MiB band. The source
        // reuses only a single template string and creates/consumes each serialized event in the callback loop.
        const int eventCount = 1_001;
        const int payloadBytes = 1_152_000;
        var store = new GeneratedStreamingTagStore(eventCount, payloadBytes);
        var stopwatch = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var peakBefore = process.PeakWorkingSet64;

        var result = await CreateService(store).ProjectTagStateAsync(MemoryTag.Instance, MemoryFoldProjector.ProjectorName);

        stopwatch.Stop();
        process.Refresh();
        Console.WriteLine(
            $"SEK-G53 manual tagged-stream telemetry: eventCount={eventCount}; payloadBytes={payloadBytes}; " +
            $"elapsedMs={stopwatch.ElapsedMilliseconds}; peakWorkingSetBytes={process.PeakWorkingSet64}; " +
            $"peakDeltaBytes={Math.Max(0, process.PeakWorkingSet64 - peakBefore)}; " +
            $"maxInFlightSerializedPayloadBytes={store.MaxInFlightSerializedPayloadBytes}; " +
            $"streamCalls={store.StreamCalls}; listCalls={store.ListCalls}");

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        Assert.Equal(eventCount, Assert.IsType<MemoryFoldState>(result.GetValue().State).Applied);
        Assert.Equal(1, store.StreamCalls);
        Assert.Equal(0, store.ListCalls);
        Assert.InRange(store.MaxInFlightSerializedPayloadBytes, 1, payloadBytes + 1024);
    }

    private static TagStateService CreateService(IEventStore eventStore)
    {
        var eventTypes = new SimpleEventTypes(JsonOptions);
        eventTypes.RegisterEventType<LargePayloadAdded>();
        var projectors = new SimpleTagProjectorTypes();
        projectors.RegisterProjector<MemoryFoldProjector>();
        return new TagStateService(
            eventStore,
            eventTypes,
            new SimpleTagTypes(),
            projectors,
            JsonOptions);
    }

    private static void CollectForMeasurement()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private sealed record LargePayloadAdded(string Payload) : IEventPayload;

    private sealed record MemoryFoldState(int Applied, int LastPayloadLength) : ITagStatePayload;

    private sealed class MemoryFoldProjector : ITagProjector<MemoryFoldProjector>
    {
        public static string ProjectorVersion => "1";
        public static string ProjectorName => nameof(MemoryFoldProjector);

        public static ITagStatePayload Project(ITagStatePayload current, Event @event)
        {
            var state = current as MemoryFoldState ?? new MemoryFoldState(0, 0);
            return @event.Payload is LargePayloadAdded added
                ? state with { Applied = state.Applied + 1, LastPayloadLength = added.Payload.Length }
                : state;
        }
    }

    private sealed class MemoryTag : ITag
    {
        public static MemoryTag Instance { get; } = new();
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => "Memory";
        public string GetTagContent() => "cold-rebuild";
        public string GetTag() => "Memory:cold-rebuild";
    }

    private abstract class GeneratedTagStoreBase : IEventStore
    {
        private readonly int _eventCount;
        private readonly string _payloadTemplate;

        protected GeneratedTagStoreBase(int eventCount, int payloadBytes)
        {
            _eventCount = eventCount;
            _payloadTemplate = new string('x', payloadBytes);
        }

        public int ListCalls { get; protected set; }
        public long PeakListPayloadBytes { get; protected set; }

        protected IEnumerable<SerializableEvent> GenerateEvents()
        {
            for (var index = 0; index < _eventCount; index++)
            {
                yield return new SerializableEvent(
                    JsonSerializer.SerializeToUtf8Bytes(new LargePayloadAdded(_payloadTemplate), JsonOptions),
                    $"tag-stream-memory-{index:D8}",
                    Guid.NewGuid(),
                    new EventMetadata("cause", "correlation", "memory"),
                    new List<string> { MemoryTag.Instance.GetTag() },
                    nameof(LargePayloadAdded));
            }
        }

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

        public virtual Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null)
        {
            ListCalls++;
            var history = GenerateEvents().ToList();
            PeakListPayloadBytes = history.Sum(@event => (long)@event.Payload.Length);
            return Task.FromResult(ResultBox.FromValue<IEnumerable<SerializableEvent>>(history));
        }

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => throw new NotSupportedException();
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => throw new NotSupportedException();
    }

    private sealed class GeneratedStreamingTagStore(int eventCount, int payloadBytes) : GeneratedTagStoreBase(eventCount, payloadBytes),
        IStreamingTaggedSerializableEventStore, ITaggedStreamCapabilityProvider
    {
        public int StreamCalls { get; private set; }
        public int MaxInFlightSerializedPayloadBytes { get; private set; }

        public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
            TaggedStreamCapabilityDescriptor.Native("generated tagged-stream memory source");

        public override Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null)
        {
            ListCalls++;
            throw new InvalidOperationException("The verified tagged-stream path must not call the list API.");
        }

        public async Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since,
            SortableUniqueId? until,
            Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default)
        {
            try
            {
                StreamCalls++;
                var count = 0;
                string? lastId = null;
                foreach (var @event in GenerateEvents())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    MaxInFlightSerializedPayloadBytes = Math.Max(MaxInFlightSerializedPayloadBytes, @event.Payload.Length);
                    await onEvent(@event);
                    cancellationToken.ThrowIfCancellationRequested();
                    count++;
                    lastId = @event.SortableUniqueIdValue;
                }

                return ResultBox.FromValue(new SerializableEventStreamReadResult(count, lastId));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return ResultBox.Error<SerializableEventStreamReadResult>(exception);
            }
        }
    }

    private sealed class GeneratedListTagStore(int eventCount, int payloadBytes) : GeneratedTagStoreBase(eventCount, payloadBytes);
}
