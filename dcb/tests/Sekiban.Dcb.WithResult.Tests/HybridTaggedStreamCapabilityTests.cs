using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     SEK-G53 Hybrid tagged-stream tripwires deliberately live in WithResult.Tests, which both net9 and net10 DCB PR
///     jobs execute. ColdEvents.Tests is not a test target in those jobs, so an equivalent test there is not CI evidence.
/// </summary>
public sealed class HybridTaggedStreamCapabilityTests
{
    [Fact]
    public async Task HybridWithSilentStreamingHotStore_FailsClosedWithoutTouchingItsListTripwire()
    {
        var hot = new ListTripwireStreamingStore();
        var hybrid = Wrap(hot);

        var resolution = SekibanDcbCapabilityResolver.ResolveTaggedStream(hybrid, "event store");
        Assert.False(resolution.IsSupported);
        Assert.False(hybrid.DescribeTaggedStream().NativeStreaming);

        var result = await hybrid.StreamSerializableEventsByTagAsync(
            HybridTag.Instance,
            null,
            null,
            _ => ValueTask.CompletedTask);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, hot.StreamCalls);
        Assert.Equal(0, hot.ListReadCalls);
    }

    [Fact]
    public async Task HybridWithHonestStreamingHotStore_IsResolvedAndForwardsOnlyTheNativeCallbackPath()
    {
        var hot = new HonestTaggedStreamStore();
        var hybrid = Wrap(hot);
        var callbackCount = 0;

        var resolution = SekibanDcbCapabilityResolver.ResolveTaggedStream(hybrid, "event store");
        Assert.True(resolution.IsSupported);
        Assert.True(hybrid.DescribeTaggedStream().NativeStreaming);

        var result = await hybrid.StreamSerializableEventsByTagAsync(
            HybridTag.Instance,
            null,
            null,
            _ =>
            {
                callbackCount++;
                return ValueTask.CompletedTask;
            });

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        Assert.Equal(1, hot.StreamCalls);
        Assert.Equal(1, callbackCount);
        Assert.Equal(0, hot.ListReadCalls);
    }

    private static HybridEventStore Wrap(IEventStore hot) =>
        new(
            hot,
            new UnusedColdObjectStorage(),
            new JsonlColdSegmentFormatHandler(),
            new DefaultServiceIdProvider(),
            Options.Create(new ColdEventStoreOptions { Enabled = true }),
            NullLogger<HybridEventStore>.Instance);

    private sealed class HybridTag : ITag
    {
        public static HybridTag Instance { get; } = new();
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => "Hybrid";
        public string GetTagContent() => "tagged-stream";
        public string GetTag() => "Hybrid:tagged-stream";
    }

    // Tagged stream resolution must finish at the hot store and never ask cold storage. A throwing implementation makes
    // that scope boundary explicit without importing the unrun ColdEvents.Tests-only test double.
    private sealed class UnusedColdObjectStorage : IColdObjectStorage
    {
        public Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct) =>
            Task.FromResult(ResultBox.Error<ColdStorageObject>(new InvalidOperationException("Cold storage is out of route.")));
        public Task<ResultBox<bool>> PutAsync(string path, Stream data, string? expectedETag, CancellationToken ct) =>
            Task.FromResult(ResultBox.Error<bool>(new InvalidOperationException("Cold storage is out of route.")));
        public Task<ResultBox<bool>> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct) =>
            Task.FromResult(ResultBox.Error<bool>(new InvalidOperationException("Cold storage is out of route.")));
        public Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct) =>
            Task.FromResult(ResultBox.Error<IReadOnlyList<string>>(new InvalidOperationException("Cold storage is out of route.")));
        public Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct) =>
            Task.FromResult(ResultBox.Error<bool>(new InvalidOperationException("Cold storage is out of route.")));
    }

    private class SilentStore : IEventStore
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
        public virtual Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => throw new NotSupportedException();
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => throw new NotSupportedException();
    }

    private sealed class ListTripwireStreamingStore : SilentStore, IStreamingTaggedSerializableEventStore
    {
        public int StreamCalls { get; private set; }
        public int ListReadCalls { get; private set; }

        public override Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null)
        {
            ListReadCalls++;
            throw new InvalidOperationException("Unsupported Hybrid must not call its hot list API.");
        }

        public Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since,
            SortableUniqueId? until,
            Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default)
        {
            StreamCalls++;
            throw new InvalidOperationException("Unsupported Hybrid must fail before forwarding a silent stream member.");
        }
    }

    private sealed class HonestTaggedStreamStore : SilentStore, IStreamingTaggedSerializableEventStore,
        ITaggedStreamCapabilityProvider
    {
        public int StreamCalls { get; private set; }
        public int ListReadCalls { get; private set; }

        public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
            TaggedStreamCapabilityDescriptor.Native("honest Hybrid hot store");

        public override Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null)
        {
            ListReadCalls++;
            throw new InvalidOperationException("The honest Hybrid stream path must not call its hot list API.");
        }

        public async Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since,
            SortableUniqueId? until,
            Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default)
        {
            StreamCalls++;
            var @event = new SerializableEvent(
                new byte[] { 1 },
                SortableUniqueId.GenerateNew(),
                Guid.NewGuid(),
                new EventMetadata("cause", "correlation", "user"),
                new List<string> { tag.GetTag() },
                "HybridEvent");
            await onEvent(@event);
            return ResultBox.FromValue(new SerializableEventStreamReadResult(1, @event.SortableUniqueIdValue));
        }
    }
}
