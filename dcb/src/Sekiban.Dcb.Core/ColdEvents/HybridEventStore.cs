using System.Diagnostics;
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
    private const string ReadAllSerializableEventsCall = nameof(ReadAllSerializableEventsAsync);
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
            var startedAtUtc = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            var hotResult = await _hotStore.ReadAllSerializableEventsAsync(since, maxCount);
            LogHybridReadOutcome(
                call: ReadAllSerializableEventsCall,
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                _serviceIdProvider.GetCurrentServiceId(),
                since,
                maxCount,
                source: "hot_only_cold_disabled",
                coldEventsRead: 0,
                hotEventsRead: hotResult.IsSuccess ? hotResult.GetValue().Count() : 0,
                coldBoundary: null,
                segmentCount: 0,
                hotResult.IsSuccess);
            return hotResult;
        }

        return await ReadHybridSerializableEventsAsync(since, maxCount);
    }

    private async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadHybridSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount)
    {
        var serviceId = _serviceIdProvider.GetCurrentServiceId();
        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var manifest = await ColdControlFileHelper.LoadManifestAsync(_coldStorage, serviceId, CancellationToken.None);

        if (manifest is null || manifest.LatestSafeSortableUniqueId is null)
        {
            _logger.LogDebug("No cold manifest found for {ServiceId}, falling back to hot store", serviceId);
            var noManifestHotResult = await _hotStore.ReadAllSerializableEventsAsync(since, maxCount);
            LogHybridReadOutcome(
                call: ReadAllSerializableEventsCall,
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                serviceId,
                since,
                maxCount,
                source: "hot_only_no_manifest",
                coldEventsRead: 0,
                hotEventsRead: noManifestHotResult.IsSuccess ? noManifestHotResult.GetValue().Count() : 0,
                coldBoundary: null,
                segmentCount: 0,
                noManifestHotResult.IsSuccess);
            return noManifestHotResult;
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
            var afterBoundaryHotResult = await _hotStore.ReadAllSerializableEventsAsync(since, maxCount);
            LogHybridReadOutcome(
                call: ReadAllSerializableEventsCall,
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                serviceId,
                since,
                maxCount,
                source: "hot_only_since_after_cold_boundary",
                coldEventsRead: 0,
                hotEventsRead: afterBoundaryHotResult.IsSuccess ? afterBoundaryHotResult.GetValue().Count() : 0,
                coldBoundary: coldBoundary.Value,
                segmentCount: manifest.Segments.Count,
                afterBoundaryHotResult.IsSuccess);
            return afterBoundaryHotResult;
        }

        var events = new List<SerializableEvent>(maxCount.GetValueOrDefault());
        var coldResult = await ReadFromColdSegmentsAsync(manifest, since, maxCount, events);
        if (!coldResult.IsSuccess)
        {
            _logger.LogWarning("Cold read failed for {ServiceId}, falling back to hot store", serviceId);
            var coldFailureHotResult = await _hotStore.ReadAllSerializableEventsAsync(since, maxCount);
            LogHybridReadOutcome(
                call: ReadAllSerializableEventsCall,
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                serviceId,
                since,
                maxCount,
                source: "hot_only_cold_read_failed",
                coldEventsRead: 0,
                hotEventsRead: coldFailureHotResult.IsSuccess ? coldFailureHotResult.GetValue().Count() : 0,
                coldBoundary: coldBoundary.Value,
                segmentCount: manifest.Segments.Count,
                coldFailureHotResult.IsSuccess);
            return coldFailureHotResult;
        }

        var coldEventsRead = events.Count;
        var remainingCount = maxCount.HasValue
            ? Math.Max(maxCount.Value - events.Count, 0)
            : (int?)null;
        if (remainingCount == 0)
        {
            LogHybridReadOutcome(
                call: ReadAllSerializableEventsCall,
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                serviceId,
                since,
                maxCount,
                source: "cold_only",
                coldEventsRead,
                hotEventsRead: 0,
                coldBoundary: coldBoundary.Value,
                segmentCount: manifest.Segments.Count,
                succeeded: true);
            return ResultBox.FromValue<IEnumerable<SerializableEvent>>(events);
        }

        var hotResult = await _hotStore.ReadAllSerializableEventsAsync(coldBoundary, remainingCount);
        if (!hotResult.IsSuccess)
        {
            LogHybridReadOutcome(
                call: ReadAllSerializableEventsCall,
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                serviceId,
                since,
                maxCount,
                source: coldEventsRead > 0 ? "cold_then_hot_failed" : "hot_only_hot_failed",
                coldEventsRead,
                hotEventsRead: 0,
                coldBoundary: coldBoundary.Value,
                segmentCount: manifest.Segments.Count,
                succeeded: false);
            return hotResult;
        }

        var hotEvents = hotResult.GetValue().ToList();
        events.AddRange(hotEvents);
        LogHybridReadOutcome(
            call: ReadAllSerializableEventsCall,
            startedAtUtc,
            stopwatch.ElapsedMilliseconds,
            serviceId,
            since,
            maxCount,
            source: hotEvents.Count > 0 && coldEventsRead > 0 ? "cold_plus_hot" : "cold_only",
            coldEventsRead,
            hotEventsRead: hotEvents.Count,
            coldBoundary: coldBoundary.Value,
            segmentCount: manifest.Segments.Count,
            succeeded: true);
        return ResultBox.FromValue<IEnumerable<SerializableEvent>>(events);
    }

    private void LogHybridReadOutcome(
        string call,
        DateTimeOffset startedAtUtc,
        long elapsedMs,
        string serviceId,
        SortableUniqueId? since,
        int? maxCount,
        string source,
        int coldEventsRead,
        int hotEventsRead,
        string? coldBoundary,
        int segmentCount,
        bool succeeded)
    {
        _logger.LogInformation(
            "Hybrid read completed. Call={Call}, StartedAtUtc={StartedAtUtc}, ServiceId={ServiceId}, Since={Since}, MaxCount={MaxCount}, Source={Source}, ColdEventsRead={ColdEventsRead}, HotEventsRead={HotEventsRead}, ColdBoundary={ColdBoundary}, SegmentCount={SegmentCount}, HotStoreType={HotStoreType}, Succeeded={Succeeded}, ElapsedMs={ElapsedMs}",
            call,
            startedAtUtc,
            serviceId,
            since?.Value ?? "beginning",
            maxCount?.ToString() ?? "all",
            source,
            coldEventsRead,
            hotEventsRead,
            coldBoundary ?? "none",
            segmentCount,
            _hotStore.GetType().Name,
            succeeded,
            elapsedMs);
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
