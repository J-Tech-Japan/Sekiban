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
            var context = StartHybridReadLogContext(_serviceIdProvider.GetCurrentServiceId(), since, maxCount);
            var hotResult = await _hotStore.ReadAllSerializableEventsAsync(since, maxCount);
            LogHybridReadOutcome(
                context,
                new HybridReadOutcome(
                    Source: "hot_only_cold_disabled",
                    ColdEventsRead: 0,
                    HotEventsRead: hotResult.IsSuccess ? TryGetEventCountWithoutEnumeration(hotResult.GetValue()) : null,
                    ColdBoundary: null,
                    SegmentCount: 0,
                    Succeeded: hotResult.IsSuccess));
            return hotResult;
        }

        return await ReadHybridSerializableEventsAsync(since, maxCount);
    }

    private async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadHybridSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount)
    {
        var serviceId = _serviceIdProvider.GetCurrentServiceId();
        var context = StartHybridReadLogContext(serviceId, since, maxCount);
        var manifest = await ColdControlFileHelper.LoadManifestAsync(_coldStorage, serviceId, CancellationToken.None);

        if (manifest is null || manifest.LatestSafeSortableUniqueId is null)
        {
            return await ReadHotWithoutManifestAsync(context);
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
            return await ReadHotAfterColdBoundaryAsync(context, manifest, coldBoundary);
        }

        return await ReadColdAndHotEventsAsync(context, manifest, coldBoundary);
    }

    private async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadHotWithoutManifestAsync(HybridReadLogContext context)
    {
        _logger.LogDebug("No cold manifest found for {ServiceId}, falling back to hot store", context.ServiceId);
        var hotResult = await _hotStore.ReadAllSerializableEventsAsync(context.Since, context.MaxCount);
        LogHybridReadOutcome(
            context,
            new HybridReadOutcome(
                Source: "hot_only_no_manifest",
                ColdEventsRead: 0,
                HotEventsRead: hotResult.IsSuccess ? TryGetEventCountWithoutEnumeration(hotResult.GetValue()) : null,
                ColdBoundary: null,
                SegmentCount: 0,
                Succeeded: hotResult.IsSuccess));
        return hotResult;
    }

    private async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadHotAfterColdBoundaryAsync(
        HybridReadLogContext context,
        ColdManifest manifest,
        SortableUniqueId coldBoundary)
    {
        _logger.LogDebug(
            "Skipping cold read for {ServiceId} because since={Since} is newer than latestSafe={LatestSafe}",
            context.ServiceId,
            context.Since!.Value,
            manifest.LatestSafeSortableUniqueId);
        var hotResult = await _hotStore.ReadAllSerializableEventsAsync(context.Since, context.MaxCount);
        LogHybridReadOutcome(
            context,
            new HybridReadOutcome(
                Source: "hot_only_since_after_cold_boundary",
                ColdEventsRead: 0,
                HotEventsRead: hotResult.IsSuccess ? TryGetEventCountWithoutEnumeration(hotResult.GetValue()) : null,
                ColdBoundary: coldBoundary.Value,
                SegmentCount: manifest.Segments.Count,
                Succeeded: hotResult.IsSuccess));
        return hotResult;
    }

    private async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadColdAndHotEventsAsync(
        HybridReadLogContext context,
        ColdManifest manifest,
        SortableUniqueId coldBoundary)
    {
        var events = new List<SerializableEvent>(context.MaxCount.GetValueOrDefault());
        var coldResult = await ReadFromColdSegmentsAsync(manifest, context.Since, context.MaxCount, events);
        if (!coldResult.IsSuccess)
        {
            return await ReadHotAfterColdReadFailureAsync(context, manifest, coldBoundary);
        }

        var coldEventsRead = events.Count;
        var remainingCount = context.MaxCount.HasValue
            ? Math.Max(context.MaxCount.Value - events.Count, 0)
            : (int?)null;
        if (remainingCount == 0)
        {
            LogHybridReadOutcome(
                context,
                new HybridReadOutcome(
                    Source: "cold_only",
                    ColdEventsRead: coldEventsRead,
                    HotEventsRead: 0,
                    ColdBoundary: coldBoundary.Value,
                    SegmentCount: manifest.Segments.Count,
                    Succeeded: true));
            return ResultBox.FromValue<IEnumerable<SerializableEvent>>(events);
        }

        var hotResult = await _hotStore.ReadAllSerializableEventsAsync(coldBoundary, remainingCount);
        if (!hotResult.IsSuccess)
        {
            LogHybridReadOutcome(
                context,
                new HybridReadOutcome(
                    Source: coldEventsRead > 0 ? "cold_then_hot_failed" : "hot_only_hot_failed",
                    ColdEventsRead: coldEventsRead,
                    HotEventsRead: 0,
                    ColdBoundary: coldBoundary.Value,
                    SegmentCount: manifest.Segments.Count,
                    Succeeded: false));
            return hotResult;
        }

        var hotEvents = hotResult.GetValue().ToList();
        events.AddRange(hotEvents);
        LogHybridReadOutcome(
            context,
            new HybridReadOutcome(
                Source: ClassifyHybridReadSource(coldEventsRead, hotEvents.Count),
                ColdEventsRead: coldEventsRead,
                HotEventsRead: hotEvents.Count,
                ColdBoundary: coldBoundary.Value,
                SegmentCount: manifest.Segments.Count,
                Succeeded: true));
        return ResultBox.FromValue<IEnumerable<SerializableEvent>>(events);
    }

    private async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadHotAfterColdReadFailureAsync(
        HybridReadLogContext context,
        ColdManifest manifest,
        SortableUniqueId coldBoundary)
    {
        _logger.LogWarning("Cold read failed for {ServiceId}, falling back to hot store", context.ServiceId);
        var hotResult = await _hotStore.ReadAllSerializableEventsAsync(context.Since, context.MaxCount);
        LogHybridReadOutcome(
            context,
            new HybridReadOutcome(
                Source: "hot_only_cold_read_failed",
                ColdEventsRead: 0,
                HotEventsRead: hotResult.IsSuccess ? TryGetEventCountWithoutEnumeration(hotResult.GetValue()) : null,
                ColdBoundary: coldBoundary.Value,
                SegmentCount: manifest.Segments.Count,
                Succeeded: hotResult.IsSuccess));
        return hotResult;
    }

    private static int? TryGetEventCountWithoutEnumeration(IEnumerable<SerializableEvent> events)
        => events.TryGetNonEnumeratedCount(out var count) ? count : null;

    private static string ClassifyHybridReadSource(int coldEventsRead, int hotEventsRead)
        => (coldEventsRead, hotEventsRead) switch
        {
            (> 0, > 0) => "cold_plus_hot",
            (> 0, 0) => "cold_only",
            (0, > 0) => "hot_only",
            _ => "empty"
        };

    private static HybridReadLogContext StartHybridReadLogContext(
        string serviceId,
        SortableUniqueId? since,
        int? maxCount)
        => new(
            serviceId,
            HybridReadProjectionContext.ProjectionName,
            since,
            maxCount,
            DateTimeOffset.UtcNow,
            Stopwatch.StartNew());

    private void LogHybridReadOutcome(HybridReadLogContext context, HybridReadOutcome outcome)
    {
        _logger.LogInformation(
            "Hybrid read completed. Call={Call}, StartedAtUtc={StartedAtUtc}, ServiceId={ServiceId}, ProjectionName={ProjectionName}, Since={Since}, MaxCount={MaxCount}, Source={Source}, ColdEventsRead={ColdEventsRead}, HotEventsRead={HotEventsRead}, ColdBoundary={ColdBoundary}, SegmentCount={SegmentCount}, HotStoreType={HotStoreType}, Succeeded={Succeeded}, ElapsedMs={ElapsedMs}",
            ReadAllSerializableEventsCall,
            context.StartedAtUtc,
            context.ServiceId,
            context.ProjectionName ?? "unknown",
            context.Since?.Value ?? "beginning",
            context.MaxCount?.ToString() ?? "all",
            outcome.Source,
            outcome.ColdEventsRead,
            outcome.HotEventsRead,
            outcome.ColdBoundary ?? "none",
            outcome.SegmentCount,
            _hotStore.GetType().Name,
            outcome.Succeeded,
            context.Stopwatch.ElapsedMilliseconds);
    }

    private sealed record HybridReadLogContext(
        string ServiceId,
        string? ProjectionName,
        SortableUniqueId? Since,
        int? MaxCount,
        DateTimeOffset StartedAtUtc,
        Stopwatch Stopwatch);

    private sealed record HybridReadOutcome(
        string Source,
        int ColdEventsRead,
        int? HotEventsRead,
        string? ColdBoundary,
        int SegmentCount,
        bool Succeeded);

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
