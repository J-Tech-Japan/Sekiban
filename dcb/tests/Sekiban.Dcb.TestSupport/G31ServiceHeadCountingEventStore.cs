using System.Collections.Concurrent;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.TestSupport;

public sealed class G31MutableServiceIdProvider(string current) : IServiceIdProvider
{
    public string Current { get; set; } = current;
    public string GetCurrentServiceId() => Current;
}

public sealed class G31ServiceHeadCountingEventStore(
    IEventStore inner,
    IServiceIdProvider serviceIdProvider) : IEventStore
{
    private readonly ConcurrentDictionary<string, int> _headReads = new(StringComparer.Ordinal);

    public int HeadReadsFor(string serviceId) => _headReads.GetValueOrDefault(serviceId);

    public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync()
    {
        _headReads.AddOrUpdate(serviceIdProvider.GetCurrentServiceId(), 1, (_, count) => count + 1);
        return inner.GetLatestSortableUniqueIdAsync();
    }

    public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => inner.ReadTagsAsync(tag);
    public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => inner.GetLatestTagAsync(tag);
    public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => inner.TagExistsAsync(tag);
    public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => inner.GetEventCountAsync(since);
    public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => inner.GetAllTagsAsync(tagGroup);
    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
        inner.ReadAllSerializableEventsAsync(since);
    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount) => inner.ReadAllSerializableEventsAsync(since, maxCount);
    public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) =>
        inner.ReadSerializableEventAsync(eventId);
    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
        ITag tag,
        SortableUniqueId? since = null) => inner.ReadSerializableEventsByTagAsync(tag, since);
    public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
        WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => inner.WriteSerializableEventsAsync(events);
}
