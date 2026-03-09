using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.ColdEvents;

public sealed class HybridEventStore : IEventStore, IStreamingSerializableEventStore
{
    private const string ReadAllSerializableEventsCall = nameof(ReadAllSerializableEventsAsync);
    private readonly IEventStore _hotStore;
    private readonly IColdObjectStorage _coldStorage;
    private readonly IColdSegmentFormatHandler _segmentFormatHandler;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly ColdEventStoreOptions _options;
    private readonly ILogger<HybridEventStore> _logger;

    public HybridEventStore(
        IEventStore hotStore,
        IColdObjectStorage coldStorage,
        IColdSegmentFormatHandler segmentFormatHandler,
        IServiceIdProvider serviceIdProvider,
        IOptions<ColdEventStoreOptions> options,
        ILogger<HybridEventStore> logger)
    {
        _hotStore = hotStore;
        _coldStorage = coldStorage;
        _segmentFormatHandler = segmentFormatHandler;
        _serviceIdProvider = serviceIdProvider;
        _options = options.Value;
        _logger = logger;
    }

    public int GetPreferredCatchUpBatchSize()
        => Math.Max(1, _options.ColdCatchUpBatchSize);

    public bool ShouldPersistSnapshotOnColdSegmentBoundary()
        => _options.PersistSnapshotOnColdSegmentBoundary;

    public int GetCatchUpPersistMaxEventsWithoutSnapshot()
        => Math.Max(1, _options.CatchUpPersistMaxEventsWithoutSnapshot);

    public TimeSpan GetCatchUpPersistMaxInterval()
        => _options.CatchUpPersistMaxInterval > TimeSpan.Zero
            ? _options.CatchUpPersistMaxInterval
            : TimeSpan.FromMinutes(5);

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
                    ReachedColdSegmentBoundary: false,
                    Succeeded: hotResult.IsSuccess));
            return hotResult;
        }

        return await ReadHybridSerializableEventsAsync(since, maxCount);
    }

    public async Task<ResultBox<SerializableEventStreamReadResult>> StreamAllSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            var context = StartHybridReadLogContext(_serviceIdProvider.GetCurrentServiceId(), since, maxCount);
            return await StreamHotOnlyAsync(
                context,
                since,
                maxCount,
                onEvent,
                cancellationToken,
                source: "hot_only_cold_disabled",
                coldBoundary: null,
                segmentCount: 0);
        }

        return await StreamHybridSerializableEventsAsync(since, maxCount, onEvent, cancellationToken);
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

    private async Task<ResultBox<SerializableEventStreamReadResult>> StreamHybridSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken)
    {
        var serviceId = _serviceIdProvider.GetCurrentServiceId();
        var context = StartHybridReadLogContext(serviceId, since, maxCount);
        var manifest = await ColdControlFileHelper.LoadManifestAsync(_coldStorage, serviceId, cancellationToken);

        if (manifest is null || manifest.LatestSafeSortableUniqueId is null)
        {
            return await StreamHotOnlyAsync(
                context,
                since,
                maxCount,
                onEvent,
                cancellationToken,
                source: "hot_only_no_manifest",
                coldBoundary: null,
                segmentCount: 0);
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
            return await StreamHotOnlyAsync(
                context,
                since,
                maxCount,
                onEvent,
                cancellationToken,
                source: "hot_only_since_after_cold_boundary",
                coldBoundary: coldBoundary.Value,
                segmentCount: manifest.Segments.Count);
        }

        return await StreamColdAndHotEventsAsync(
            context,
            manifest,
            coldBoundary,
            onEvent,
            cancellationToken);
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
                ReachedColdSegmentBoundary: false,
                Succeeded: hotResult.IsSuccess));
        return hotResult;
    }

    private async Task<ResultBox<SerializableEventStreamReadResult>> StreamHotOnlyAsync(
        HybridReadLogContext context,
        SortableUniqueId? since,
        int? maxCount,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken,
        string source,
        string? coldBoundary,
        int segmentCount)
    {
        if (source == "hot_only_no_manifest")
        {
            _logger.LogDebug("No cold manifest found for {ServiceId}, falling back to hot store", context.ServiceId);
        }
        else if (source == "hot_only_since_after_cold_boundary" && coldBoundary is not null)
        {
            _logger.LogDebug(
                "Skipping cold read for {ServiceId} because since={Since} is newer than latestSafe={LatestSafe}",
                context.ServiceId,
                since!.Value,
                coldBoundary);
        }

        var hotResult = await _hotStore.ReadAllSerializableEventsAsync(since, maxCount);
        if (!hotResult.IsSuccess)
        {
            LogHybridReadOutcome(
                context,
                new HybridReadOutcome(
                    Source: source,
                    ColdEventsRead: 0,
                    HotEventsRead: null,
                    ColdBoundary: coldBoundary,
                    SegmentCount: segmentCount,
                    ReachedColdSegmentBoundary: false,
                    Succeeded: false));
            return ResultBox.Error<SerializableEventStreamReadResult>(hotResult.GetException());
        }

        var streamResult = await StreamEnumerableAsync(
            hotResult.GetValue(),
            onEvent,
            cancellationToken);
        LogHybridReadOutcome(
            context,
            new HybridReadOutcome(
                Source: source,
                ColdEventsRead: 0,
                HotEventsRead: streamResult.EventsRead,
                ColdBoundary: coldBoundary,
                SegmentCount: segmentCount,
                ReachedColdSegmentBoundary: false,
                Succeeded: true));
        return ResultBox.FromValue(streamResult);
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
                ReachedColdSegmentBoundary: false,
                Succeeded: hotResult.IsSuccess));
        return hotResult;
    }

    private async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadColdAndHotEventsAsync(
        HybridReadLogContext context,
        ColdManifest manifest,
        SortableUniqueId coldBoundary)
    {
        var events = new List<SerializableEvent>(context.MaxCount.GetValueOrDefault());
        var alignToSegmentBoundary = ShouldAlignCatchUpReadsToSegmentBoundary();
        var coldResult = await ReadFromColdSegmentsAsync(
            manifest,
            context.Since,
            context.MaxCount,
            events,
            alignToSegmentBoundary);
        if (!coldResult.IsSuccess)
        {
            return await ReadHotAfterColdReadFailureAsync(context, manifest, coldBoundary);
        }

        var coldRead = coldResult.GetValue();
        var coldEventsRead = coldRead.ColdEventsRead;
        var remainingCount = context.MaxCount.HasValue
            ? Math.Max(context.MaxCount.Value - events.Count, 0)
            : (int?)null;
        var stopAtColdBoundary = alignToSegmentBoundary && coldRead.ColdEventsRead > 0;
        if (remainingCount == 0 || stopAtColdBoundary)
        {
            LogHybridReadOutcome(
                context,
                new HybridReadOutcome(
                    Source: "cold_only",
                    ColdEventsRead: coldEventsRead,
                    HotEventsRead: 0,
                    ColdBoundary: coldBoundary.Value,
                    SegmentCount: manifest.Segments.Count,
                    ReachedColdSegmentBoundary: coldRead.ReachedColdSegmentBoundary,
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
                    ReachedColdSegmentBoundary: coldRead.ReachedColdSegmentBoundary,
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
                ReachedColdSegmentBoundary: coldRead.ReachedColdSegmentBoundary,
                Succeeded: true));
        return ResultBox.FromValue<IEnumerable<SerializableEvent>>(events);
    }

    private async Task<ResultBox<SerializableEventStreamReadResult>> StreamColdAndHotEventsAsync(
        HybridReadLogContext context,
        ColdManifest manifest,
        SortableUniqueId coldBoundary,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken)
    {
        var alignToSegmentBoundary = ShouldAlignCatchUpReadsToSegmentBoundary();
        var coldResult = await StreamFromColdSegmentsAsync(
            manifest,
            context.Since,
            context.MaxCount,
            onEvent,
            cancellationToken,
            alignToSegmentBoundary);
        if (!coldResult.IsSuccess)
        {
            return await StreamHotAfterColdReadFailureAsync(
                context,
                manifest,
                coldBoundary,
                onEvent,
                cancellationToken);
        }

        var coldRead = coldResult.GetValue();
        var remainingCount = context.MaxCount.HasValue
            ? Math.Max(context.MaxCount.Value - coldRead.EventsRead, 0)
            : (int?)null;
        var stopAtColdBoundary = alignToSegmentBoundary && coldRead.EventsRead > 0;
        if (remainingCount == 0 || stopAtColdBoundary)
        {
            LogHybridReadOutcome(
                context,
                new HybridReadOutcome(
                    Source: "cold_only",
                    ColdEventsRead: coldRead.EventsRead,
                    HotEventsRead: 0,
                    ColdBoundary: coldBoundary.Value,
                    SegmentCount: manifest.Segments.Count,
                    ReachedColdSegmentBoundary: coldRead.ReachedColdSegmentBoundary,
                    Succeeded: true));
            return ResultBox.FromValue(
                new SerializableEventStreamReadResult(
                    coldRead.EventsRead,
                    coldRead.LastSortableUniqueId));
        }

        var hotResult = await _hotStore.ReadAllSerializableEventsAsync(coldBoundary, remainingCount);
        if (!hotResult.IsSuccess)
        {
            LogHybridReadOutcome(
                context,
                new HybridReadOutcome(
                    Source: coldRead.EventsRead > 0 ? "cold_then_hot_failed" : "hot_only_hot_failed",
                    ColdEventsRead: coldRead.EventsRead,
                    HotEventsRead: 0,
                    ColdBoundary: coldBoundary.Value,
                    SegmentCount: manifest.Segments.Count,
                    ReachedColdSegmentBoundary: coldRead.ReachedColdSegmentBoundary,
                    Succeeded: false));
            return ResultBox.Error<SerializableEventStreamReadResult>(hotResult.GetException());
        }

        var hotStreamResult = await StreamEnumerableAsync(
            hotResult.GetValue(),
            onEvent,
            cancellationToken);
        var totalEventsRead = coldRead.EventsRead + hotStreamResult.EventsRead;
        LogHybridReadOutcome(
            context,
            new HybridReadOutcome(
                Source: ClassifyHybridReadSource(coldRead.EventsRead, hotStreamResult.EventsRead),
                ColdEventsRead: coldRead.EventsRead,
                HotEventsRead: hotStreamResult.EventsRead,
                ColdBoundary: coldBoundary.Value,
                SegmentCount: manifest.Segments.Count,
                ReachedColdSegmentBoundary: coldRead.ReachedColdSegmentBoundary,
                Succeeded: true));
        return ResultBox.FromValue(
            new SerializableEventStreamReadResult(
                totalEventsRead,
                hotStreamResult.LastSortableUniqueId ?? coldRead.LastSortableUniqueId));
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
                ReachedColdSegmentBoundary: false,
                Succeeded: hotResult.IsSuccess));
        return hotResult;
    }

    private async Task<ResultBox<SerializableEventStreamReadResult>> StreamHotAfterColdReadFailureAsync(
        HybridReadLogContext context,
        ColdManifest manifest,
        SortableUniqueId coldBoundary,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("Cold read failed for {ServiceId}, falling back to hot store", context.ServiceId);
        var hotResult = await _hotStore.ReadAllSerializableEventsAsync(context.Since, context.MaxCount);
        if (!hotResult.IsSuccess)
        {
            LogHybridReadOutcome(
                context,
                new HybridReadOutcome(
                    Source: "hot_only_cold_read_failed",
                    ColdEventsRead: 0,
                    HotEventsRead: null,
                    ColdBoundary: coldBoundary.Value,
                    SegmentCount: manifest.Segments.Count,
                    ReachedColdSegmentBoundary: false,
                    Succeeded: false));
            return ResultBox.Error<SerializableEventStreamReadResult>(hotResult.GetException());
        }

        var streamResult = await StreamEnumerableAsync(
            hotResult.GetValue(),
            onEvent,
            cancellationToken);
        LogHybridReadOutcome(
            context,
            new HybridReadOutcome(
                Source: "hot_only_cold_read_failed",
                ColdEventsRead: 0,
                HotEventsRead: streamResult.EventsRead,
                ColdBoundary: coldBoundary.Value,
                SegmentCount: manifest.Segments.Count,
                ReachedColdSegmentBoundary: false,
                Succeeded: true));
        return ResultBox.FromValue(streamResult);
    }

    private static int? TryGetEventCountWithoutEnumeration(IEnumerable<SerializableEvent> events)
        => events.TryGetNonEnumeratedCount(out var count) ? count : null;

    private static async Task<SerializableEventStreamReadResult> StreamEnumerableAsync(
        IEnumerable<SerializableEvent> events,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken)
    {
        var count = 0;
        string? lastSortableUniqueId = null;
        foreach (var evt in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await onEvent(evt);
            count++;
            lastSortableUniqueId = evt.SortableUniqueIdValue;
        }

        return new SerializableEventStreamReadResult(count, lastSortableUniqueId);
    }

    private static string ClassifyHybridReadSource(int coldEventsRead, int hotEventsRead)
        => (coldEventsRead, hotEventsRead) switch
        {
            (> 0, > 0) => "cold_plus_hot",
            (> 0, 0) => "cold_only",
            (0, > 0) => "hot_only",
            _ => "empty"
        };

    private bool ShouldAlignCatchUpReadsToSegmentBoundary()
        => _options.AlignCatchUpReadsToSegmentBoundary
           && !string.IsNullOrWhiteSpace(HybridReadProjectionContext.ProjectionName);

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
        if (!string.IsNullOrWhiteSpace(HybridReadProjectionContext.ProjectionName))
        {
            HybridReadProjectionContext.SetBatchMetadata(
                new HybridReadBatchMetadata(
                    outcome.Source,
                    UsedCold: outcome.ColdEventsRead > 0,
                    UsedHot: outcome.HotEventsRead.GetValueOrDefault() > 0,
                    outcome.ReachedColdSegmentBoundary,
                    outcome.ColdEventsRead,
                    outcome.HotEventsRead ?? 0,
                    outcome.SegmentCount));
        }

        _logger.LogInformation(
            "Hybrid read completed. Call={Call}, StartedAtUtc={StartedAtUtc}, ServiceId={ServiceId}, ProjectionName={ProjectionName}, Since={Since}, MaxCount={MaxCount}, Source={Source}, ColdEventsRead={ColdEventsRead}, HotEventsRead={HotEventsRead}, ColdBoundary={ColdBoundary}, SegmentCount={SegmentCount}, ReachedColdSegmentBoundary={ReachedColdSegmentBoundary}, HotStoreType={HotStoreType}, Succeeded={Succeeded}, ElapsedMs={ElapsedMs}",
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
            outcome.ReachedColdSegmentBoundary,
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
        bool ReachedColdSegmentBoundary,
        bool Succeeded);

    private async Task<ResultBox<ColdReadResult>> ReadFromColdSegmentsAsync(
        ColdManifest manifest,
        SortableUniqueId? since,
        int? maxCount,
        List<SerializableEvent> destination,
        bool alignToSegmentBoundary)
    {
        foreach (var segment in manifest.Segments.OrderBy(s => s.FromSortableUniqueId, StringComparer.Ordinal))
        {
            if (ShouldSkipColdSegment(segment, since))
            {
                continue;
            }

            var remainingCount = GetRemainingColdReadCount(maxCount, destination.Count);
            if (remainingCount == 0)
            {
                return ResultBox.FromValue(
                    new ColdReadResult(destination.Count, ReachedColdSegmentBoundary: false));
            }

            var appendResult = await TryAppendColdSegmentAsync(segment, since, remainingCount, destination);
            if (!appendResult.IsSuccess)
            {
                return ResultBox.Error<ColdReadResult>(appendResult.GetException());
            }

            var coldSegmentResult = appendResult.GetValue();
            if (coldSegmentResult.EventsRead == 0)
            {
                continue;
            }

            if (ShouldStopReadingColdSegments(maxCount, destination.Count, alignToSegmentBoundary))
            {
                return ResultBox.FromValue(
                    new ColdReadResult(destination.Count, coldSegmentResult.ReachedEndOfSegment));
            }
        }

        return ResultBox.FromValue(new ColdReadResult(destination.Count, ReachedColdSegmentBoundary: false));
    }

    private static bool ShouldSkipColdSegment(ColdSegmentInfo segment, SortableUniqueId? since)
        => since is not null
           && string.Compare(segment.ToSortableUniqueId, since.Value, StringComparison.Ordinal) <= 0;

    private static int? GetRemainingColdReadCount(int? maxCount, int currentCount)
        => maxCount.HasValue
            ? Math.Max(maxCount.Value - currentCount, 0)
            : null;

    private static bool ShouldStopReadingColdSegments(int? maxCount, int currentCount, bool alignToSegmentBoundary)
        => alignToSegmentBoundary || (maxCount.HasValue && currentCount >= maxCount.Value);

    private async Task<ResultBox<ColdSegmentStreamResult>> TryAppendColdSegmentAsync(
        ColdSegmentInfo segment,
        SortableUniqueId? since,
        int? remainingCount,
        List<SerializableEvent> destination)
    {
        var streamResult = await _coldStorage.OpenReadAsync(segment.Path, CancellationToken.None);
        if (!streamResult.IsSuccess)
        {
            _logger.LogWarning("Failed to read cold segment {Path}", segment.Path);
            return ResultBox.Error<ColdSegmentStreamResult>(streamResult.GetException());
        }

        await using var stream = streamResult.GetValue();
        var parseResult = await _segmentFormatHandler.StreamSegmentAsync(
            stream,
            since,
            remainingCount,
            evt =>
            {
                destination.Add(evt);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);
        if (!parseResult.IsSuccess)
        {
            _logger.LogWarning(
                parseResult.GetException(),
                "Failed to parse cold segment {Path}",
                segment.Path);
            return ResultBox.Error<ColdSegmentStreamResult>(parseResult.GetException());
        }

        return ResultBox.FromValue(parseResult.GetValue());
    }

    private sealed record ColdReadResult(
        int ColdEventsRead,
        bool ReachedColdSegmentBoundary);

    private sealed record ColdStreamReadResult(
        int EventsRead,
        string? LastSortableUniqueId,
        bool ReachedColdSegmentBoundary);

    private async Task<ResultBox<ColdStreamReadResult>> StreamFromColdSegmentsAsync(
        ColdManifest manifest,
        SortableUniqueId? since,
        int? maxCount,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken,
        bool alignToSegmentBoundary)
    {
        var totalEventsRead = 0;
        string? lastSortableUniqueId = null;
        foreach (var segment in manifest.Segments.OrderBy(s => s.FromSortableUniqueId, StringComparer.Ordinal))
        {
            if (since is not null
                && string.Compare(segment.ToSortableUniqueId, since.Value, StringComparison.Ordinal) <= 0)
            {
                continue;
            }

            var streamResult = await _coldStorage.OpenReadAsync(segment.Path, cancellationToken);
            if (!streamResult.IsSuccess)
            {
                _logger.LogWarning("Failed to read cold segment {Path}", segment.Path);
                return ResultBox.Error<ColdStreamReadResult>(streamResult.GetException());
            }

            await using var stream = streamResult.GetValue();
            var remainingCount = maxCount.HasValue
                ? Math.Max(maxCount.Value - totalEventsRead, 0)
                : (int?)null;
            var parseResult = await _segmentFormatHandler.StreamSegmentAsync(
                stream,
                since,
                remainingCount,
                onEvent,
                cancellationToken);
            if (!parseResult.IsSuccess)
            {
                _logger.LogWarning(
                    parseResult.GetException(),
                    "Failed to parse cold segment {Path}",
                    segment.Path);
                return ResultBox.Error<ColdStreamReadResult>(parseResult.GetException());
            }

            var appendResult = parseResult.GetValue();
            if (appendResult.EventsRead == 0)
            {
                continue;
            }

            totalEventsRead += appendResult.EventsRead;
            lastSortableUniqueId = appendResult.LastSortableUniqueId ?? lastSortableUniqueId;
            if (maxCount.HasValue && totalEventsRead >= maxCount.Value)
            {
                return ResultBox.FromValue(
                    new ColdStreamReadResult(
                        totalEventsRead,
                        lastSortableUniqueId,
                        appendResult.ReachedEndOfSegment));
            }

            if (alignToSegmentBoundary)
            {
                return ResultBox.FromValue(
                    new ColdStreamReadResult(
                        totalEventsRead,
                        lastSortableUniqueId,
                        appendResult.ReachedEndOfSegment));
            }
        }

        return ResultBox.FromValue(
            new ColdStreamReadResult(
                totalEventsRead,
                lastSortableUniqueId,
                ReachedColdSegmentBoundary: false));
    }

}
