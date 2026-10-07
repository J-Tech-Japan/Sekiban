using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.ColdEvents.Tests;

internal sealed class ListEventStore : IEventStore
{
    private readonly IReadOnlyList<SerializableEvent> _events;
    private readonly bool _ignoreSinceFilter;

    public ListEventStore(
        IReadOnlyList<SerializableEvent> events,
        bool ignoreSinceFilter = false)
    {
        _events = events;
        _ignoreSinceFilter = ignoreSinceFilter;
    }

    public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
        => throw new NotSupportedException();

    public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
        => throw new NotSupportedException();

    public Task<ResultBox<bool>> TagExistsAsync(ITag tag)
        => throw new NotSupportedException();

    public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null)
        => throw new NotSupportedException();

    public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null)
        => throw new NotSupportedException();

    public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync()
        => throw new NotSupportedException();

    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null)
    {
        IEnumerable<SerializableEvent> filtered = _ignoreSinceFilter || since is null
            ? _events
            : _events.Where(e => string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
        return Task.FromResult(ResultBox.FromValue(filtered));
    }

    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount)
    {
        IEnumerable<SerializableEvent> filtered = _ignoreSinceFilter || since is null
            ? _events
            : _events.Where(e => string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
        if (maxCount.HasValue)
        {
            filtered = filtered.Take(maxCount.Value);
        }
        return Task.FromResult(ResultBox.FromValue(filtered));
    }

    public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId)
    {
        var result = _events.FirstOrDefault(e => e.Id == eventId);
        return result == null
            ? Task.FromResult(ResultBox.Error<SerializableEvent>(new KeyNotFoundException($"Event {eventId} not found")))
            : Task.FromResult(ResultBox.FromValue(result));
    }

    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
        ITag tag,
        SortableUniqueId? since = null)
        => throw new NotSupportedException();

    public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
        WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events)
        => throw new NotSupportedException();
}
