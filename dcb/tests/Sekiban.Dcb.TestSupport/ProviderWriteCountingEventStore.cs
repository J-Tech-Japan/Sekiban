using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.TestSupport;

/// <summary>
///     Transparent test observer around a real provider. It deliberately does not add any write capability: expected
///     position requests must be rejected by the normal production capability gate, while a facade that accidentally
///     drops that request would reach <see cref="WriteSerializableEventsAsync"/> and make the failure visible.
/// </summary>
public sealed class ProviderWriteCountingEventStore : IEventStore, IWriteConditionCapabilityProvider
{
    private readonly IEventStore _inner;
    private readonly string _fallbackProviderName;
    private int _describeCalls;
    private int _providerWriteCalls;

    public ProviderWriteCountingEventStore(IEventStore inner, string fallbackProviderName)
    {
        _inner = inner;
        _fallbackProviderName = fallbackProviderName;
    }

    public int DescribeCalls => Volatile.Read(ref _describeCalls);
    public int ProviderWriteCalls => Volatile.Read(ref _providerWriteCalls);

    public WriteConditionCapabilityDescriptor DescribeWriteConditions()
    {
        Interlocked.Increment(ref _describeCalls);
        return _inner is IWriteConditionCapabilityProvider capabilityProvider
            ? capabilityProvider.DescribeWriteConditions()
            : WriteConditionCapabilityDescriptor.None(_fallbackProviderName);
    }

    public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => _inner.ReadTagsAsync(tag);
    public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => _inner.GetLatestTagAsync(tag);
    public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => _inner.TagExistsAsync(tag);
    public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => _inner.GetEventCountAsync(since);
    public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => _inner.GetAllTagsAsync(tagGroup);
    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
        _inner.ReadAllSerializableEventsAsync(since);
    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount) => _inner.ReadAllSerializableEventsAsync(since, maxCount);
    public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) =>
        _inner.ReadSerializableEventAsync(eventId);
    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
        ITag tag,
        SortableUniqueId? since = null) => _inner.ReadSerializableEventsByTagAsync(tag, since);

    public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
        WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events)
    {
        Interlocked.Increment(ref _providerWriteCalls);
        return _inner.WriteSerializableEventsAsync(events);
    }

    public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => _inner.GetLatestSortableUniqueIdAsync();
}
