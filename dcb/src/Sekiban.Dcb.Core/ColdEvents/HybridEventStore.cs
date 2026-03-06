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

    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
        ITag tag, SortableUniqueId? since = null)
        => _hotStore.ReadSerializableEventsByTagAsync(tag, since);

    public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId)
        => _hotStore.ReadSerializableEventAsync(eventId);

    public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
        WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events)
        => _hotStore.WriteSerializableEventsAsync(events);

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

        var events = new List<SerializableEvent>(maxCount.GetValueOrDefault());
        var coldResult = await ReadFromColdSegmentsAsync(manifest, since, maxCount, events);
        if (!coldResult.IsSuccess)
        {
            _logger.LogWarning("Cold read failed for {ServiceId}, falling back to hot store", serviceId);
            return await _hotStore.ReadAllSerializableEventsAsync(since, maxCount);
        }

        var remainingCount = maxCount.HasValue
            ? Math.Max(maxCount.Value - events.Count, 0)
            : (int?)null;
        if (remainingCount == 0)
        {
            return ResultBox.FromValue<IEnumerable<SerializableEvent>>(events);
        }

        var hotResult = await _hotStore.ReadAllSerializableEventsAsync(coldBoundary, remainingCount);
        if (!hotResult.IsSuccess)
        {
            return hotResult;
        }

        events.AddRange(hotResult.GetValue());
        return ResultBox.FromValue<IEnumerable<SerializableEvent>>(events);
    }

    private async Task<ResultBox<bool>> ReadFromColdSegmentsAsync(
        ColdManifest manifest,
        SortableUniqueId? since,
        int? maxCount,
        List<SerializableEvent> destination)
    {
        foreach (var segment in manifest.Segments.OrderBy(s => s.FromSortableUniqueId, StringComparer.Ordinal))
        {
            if (since is not null
                && string.Compare(segment.ToSortableUniqueId, since.Value, StringComparison.Ordinal) <= 0)
            {
                continue;
            }

            var streamResult = await _coldStorage.OpenReadAsync(segment.Path, CancellationToken.None);
            if (!streamResult.IsSuccess)
            {
                _logger.LogWarning("Failed to read cold segment {Path}", segment.Path);
                return ResultBox.Error<bool>(streamResult.GetException());
            }

            await using var stream = streamResult.GetValue();
            var parseResult = await AppendJsonlSegmentAsync(stream, since, maxCount, destination);
            if (!parseResult.IsSuccess)
            {
                _logger.LogWarning("Failed to parse cold segment {Path}", segment.Path);
                return parseResult;
            }

            if (maxCount.HasValue && destination.Count >= maxCount.Value)
            {
                break;
            }
        }

        return ResultBox.FromValue(true);
    }

    private static async Task<ResultBox<bool>> AppendJsonlSegmentAsync(
        Stream data,
        SortableUniqueId? since,
        int? maxCount,
        List<SerializableEvent> destination)
    {
        using var reader = new StreamReader(data, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var evt = JsonSerializer.Deserialize<SerializableEvent>(line, ColdEventJsonOptions.Default);
            if (evt is null)
            {
                return ResultBox.Error<bool>(new InvalidDataException("Cold JSONL segment line deserialized to null."));
            }

            if (since is not null
                && string.Compare(evt.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) <= 0)
            {
                continue;
            }

            destination.Add(evt);
            if (maxCount.HasValue && destination.Count >= maxCount.Value)
            {
                break;
            }
        }

        return ResultBox.FromValue(true);
    }

}
