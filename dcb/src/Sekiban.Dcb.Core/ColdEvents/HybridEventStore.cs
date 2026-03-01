using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.ColdEvents;

public sealed class HybridEventStore : IEventStore
{
    private readonly IEventStore _hotStore;
    private readonly IColdObjectStorage _coldStorage;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly ColdEventStoreOptions _options;
    private readonly ILogger<HybridEventStore> _logger;

    public HybridEventStore(
        IEventStore hotStore,
        IColdObjectStorage coldStorage,
        IServiceIdProvider serviceIdProvider,
        IOptions<ColdEventStoreOptions> options,
        ILogger<HybridEventStore> logger)
    {
        _hotStore = hotStore;
        _coldStorage = coldStorage;
        _serviceIdProvider = serviceIdProvider;
        _options = options.Value;
        _logger = logger;
    }

    public Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(
        IEnumerable<Event> events)
        => _hotStore.WriteEventsAsync(events);

    public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
        => _hotStore.ReadTagsAsync(tag);

    public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
        => _hotStore.GetLatestTagAsync(tag);

    public Task<ResultBox<bool>> TagExistsAsync(ITag tag)
        => _hotStore.TagExistsAsync(tag);

    public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null)
        => _hotStore.GetEventCountAsync(since);

    public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null)
        => _hotStore.GetAllTagsAsync(tagGroup);

    public Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
        => _hotStore.ReadEventsByTagAsync(tag, since);

    public Task<ResultBox<Event>> ReadEventAsync(Guid eventId)
        => _hotStore.ReadEventAsync(eventId);

    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
        ITag tag, SortableUniqueId? since = null)
        => _hotStore.ReadSerializableEventsByTagAsync(tag, since);

    public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
        WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events)
        => _hotStore.WriteSerializableEventsAsync(events);

    public async Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(
        SortableUniqueId? since = null, int? maxCount = null)
        => await _hotStore.ReadAllEventsAsync(since, maxCount);

    public async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
        SortableUniqueId? since = null)
        => await ReadAllSerializableEventsAsync(since, maxCount: null);

    public async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount)
    {
        if (!_options.Enabled)
        {
            return await _hotStore.ReadAllSerializableEventsAsync(since, maxCount);
        }

        return await ReadHybridSerializableEventsAsync(since, maxCount);
    }

    private async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadHybridSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount)
    {
        var serviceId = _serviceIdProvider.GetCurrentServiceId();
        var manifest = await ColdControlFileHelper.LoadManifestAsync(_coldStorage, serviceId, CancellationToken.None);

        if (manifest is null || manifest.LatestSafeSortableUniqueId is null)
        {
            _logger.LogDebug("No cold manifest found for {ServiceId}, falling back to hot store", serviceId);
            return await _hotStore.ReadAllSerializableEventsAsync(since, maxCount);
        }

        var coldBoundary = new SortableUniqueId(manifest.LatestSafeSortableUniqueId);
        _logger.LogDebug(
            "Using cold manifest for {ServiceId}: latestSafe={LatestSafe}, segments={SegmentCount}, since={Since}",
            serviceId,
            manifest.LatestSafeSortableUniqueId,
            manifest.Segments.Count,
            since?.Value);

        if (since is not null && since.IsLaterThan(coldBoundary))
        {
            _logger.LogDebug(
                "Skipping cold read for {ServiceId} because since={Since} is newer than latestSafe={LatestSafe}",
                serviceId,
                since.Value,
                manifest.LatestSafeSortableUniqueId);
            return await _hotStore.ReadAllSerializableEventsAsync(since, maxCount);
        }

        var coldEvents = await ReadFromColdSegmentsAsync(manifest, since);
        if (coldEvents is null)
        {
            _logger.LogWarning("Cold read failed for {ServiceId}, falling back to hot store", serviceId);
            return await _hotStore.ReadAllSerializableEventsAsync(since, maxCount);
        }

        var hotResult = await _hotStore.ReadAllSerializableEventsAsync(coldBoundary, maxCount: null);
        if (!hotResult.IsSuccess)
        {
            return hotResult;
        }

        var merged = coldEvents
            .Concat(hotResult.GetValue())
            .DistinctBy(e => e.Id)
            .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal);

        IEnumerable<SerializableEvent> result = maxCount.HasValue
            ? merged.Take(maxCount.Value)
            : merged;

        return ResultBox.FromValue(result);
    }

    private async Task<List<SerializableEvent>?> ReadFromColdSegmentsAsync(
        ColdManifest manifest,
        SortableUniqueId? since)
    {
        var events = new List<SerializableEvent>();

        foreach (var segment in manifest.Segments)
        {
            if (since is not null
                && string.Compare(segment.ToSortableUniqueId, since.Value, StringComparison.Ordinal) <= 0)
            {
                continue;
            }

            var getResult = await _coldStorage.GetAsync(segment.Path, CancellationToken.None);
            if (!getResult.IsSuccess)
            {
                _logger.LogWarning("Failed to read cold segment {Path}", segment.Path);
                return null;
            }

            var segmentEvents = ParseJsonlSegment(getResult.GetValue().Data);
            if (segmentEvents is null)
            {
                _logger.LogWarning("Failed to parse cold segment {Path}", segment.Path);
                return null;
            }

            foreach (var e in segmentEvents)
            {
                if (since is not null
                    && string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) <= 0)
                {
                    continue;
                }
                events.Add(e);
            }
        }

        return events;
    }

    private static List<SerializableEvent>? ParseJsonlSegment(byte[] data)
    {
        var text = System.Text.Encoding.UTF8.GetString(data);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var events = new List<SerializableEvent>(lines.Length);

        foreach (var line in lines)
        {
            var evt = JsonSerializer.Deserialize<SerializableEvent>(line, ColdEventJsonOptions.Default);
            if (evt is null)
            {
                return null;
            }
            events.Add(evt);
        }

        return events;
    }

}
