using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.ColdEvents;

public sealed class HybridEventStore : IEventStore, IStreamingSerializableEventStore,
    IStreamingTaggedSerializableEventStore, IStorageDurabilityDescriptorProvider, IWriteConditionCapabilityProvider,
    ITaggedStreamCapabilityProvider, IConditionalEventStore, IExpectedTagPositionEventStore
{
    private const string ReadAllSerializableEventsCall = nameof(ReadAllSerializableEventsAsync);

    public string? ExpectedTagPositionServiceId =>
        (_hotStore as IExpectedTagPositionEventStore)?.ExpectedTagPositionServiceId;

    /// <summary>
    ///     A wrapper is not a durability. Writes land in the hot store, so the hot store's answer is the answer — and if
    ///     the hot store will not say, neither will we. Reporting "Durable" because a durable-sounding decorator was
    ///     applied is precisely the mistake this whole mechanism exists to make impossible.
    /// </summary>
    public StorageDurabilityDescriptor DescribeStorage()
    {
        var hot = SekibanDcbCapabilityResolver.DescribeStorage(_hotStore, "hot event store");
        return new StorageDurabilityDescriptor(hot.Durability, $"HybridEventStore({hot.ProviderName})");
    }

    /// <summary>
    ///     Writes land in the hot store, so write-conditions are exactly what the hot store enforces — the same
    ///     never-upgrade rule as <see cref="DescribeStorage" />. If the hot store cannot condition a write, neither can
    ///     the Hybrid.
    /// </summary>
    public WriteConditionCapabilityDescriptor DescribeWriteConditions()
    {
        var hot = SekibanDcbCapabilityResolver.DescribeWriteConditions(_hotStore, "hot event store");

        // Advertise every kind only when the hot store BOTH declares it AND this decorator can forward the corresponding
        // optional interface. A descriptor-only/deceptive hot store must never make the executor's preflight succeed.
        var forwardable = hot.SupportedKinds.Where(kind => kind switch
        {
            WriteConditionKind.SingleEventUniqueKey => _hotStore is IConditionalEventStore,
            WriteConditionKind.ExpectedTagPosition => _hotStore is IExpectedTagPositionEventStore,
            _ => false
        }).ToHashSet();
        return new WriteConditionCapabilityDescriptor(forwardable, $"HybridEventStore({hot.ProviderName})");
    }

    /// <summary>
    ///     Tagged streaming is honest only when the live hot store both exposes the optional method and declares native
    ///     streaming. The Hybrid's unconditional interface membership must not turn a list-backed hot store into a false
    ///     positive.
    /// </summary>
    public TaggedStreamCapabilityDescriptor DescribeTaggedStream()
    {
        var hot = SekibanDcbCapabilityResolver.ResolveTaggedStream(_hotStore, "hot event store");
        var providerName = $"HybridEventStore({hot.Descriptor.ProviderName})";
        return hot.IsSupported
            ? TaggedStreamCapabilityDescriptor.Native(providerName)
            : TaggedStreamCapabilityDescriptor.None(providerName);
    }

    /// <summary>
    ///     Forwards a conditional append to the hot store. This is only reachable when the hot store is itself an
    ///     <see cref="IConditionalEventStore" /> (otherwise <see cref="DescribeWriteConditions" /> reports nothing and the
    ///     caller fails closed before getting here); the guard is defensive.
    /// </summary>
    public Task<ConditionalAppendReceipt> AppendIfUniqueAsync(
        ConditionalAppendRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_hotStore is IConditionalEventStore conditionalHot)
        {
            return conditionalHot.AppendIfUniqueAsync(request, cancellationToken);
        }

        var descriptor = SekibanDcbCapabilityResolver.DescribeWriteConditions(_hotStore, "hot event store");
        return Task.FromException<ConditionalAppendReceipt>(
            new ConditionNotSupportedException(
                WriteConditionKind.SingleEventUniqueKey,
                $"HybridEventStore({descriptor.ProviderName})"));
    }

    /// <inheritdoc />
    public Task<bool> EnsureExpectedTagPositionEnforcementEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        if (_hotStore is IExpectedTagPositionEventStore expectedHot)
        {
            return expectedHot.EnsureExpectedTagPositionEnforcementEnabledAsync(cancellationToken);
        }

        var descriptor = SekibanDcbCapabilityResolver.DescribeWriteConditions(_hotStore, "hot event store");
        return Task.FromException<bool>(new ConditionNotSupportedException(
            WriteConditionKind.ExpectedTagPosition,
            $"HybridEventStore({descriptor.ProviderName})"));
    }

    /// <inheritdoc />
    public Task<ExpectedTagPositionWriteResult> WriteSerializableEventsWithExpectedTagPositionsAsync(
        IReadOnlyList<SerializableEvent> events,
        ExpectedTagPositionSpecification specification,
        CancellationToken cancellationToken = default)
    {
        if (_hotStore is IExpectedTagPositionEventStore expectedHot)
        {
            return expectedHot.WriteSerializableEventsWithExpectedTagPositionsAsync(events, specification, cancellationToken);
        }

        var descriptor = SekibanDcbCapabilityResolver.DescribeWriteConditions(_hotStore, "hot event store");
        return Task.FromException<ExpectedTagPositionWriteResult>(new ConditionNotSupportedException(
            WriteConditionKind.ExpectedTagPosition,
            $"HybridEventStore({descriptor.ProviderName})"));
    }
    private readonly RetainedColdSegmentHolder _retainedSegment;
    private readonly TimeProvider _timeProvider;
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
        : this(hotStore, coldStorage, segmentFormatHandler, serviceIdProvider, options, logger,
            new RetainedColdSegmentHolder(), TimeProvider.System)
    {
    }

    internal HybridEventStore(
        IEventStore hotStore,
        IColdObjectStorage coldStorage,
        IColdSegmentFormatHandler segmentFormatHandler,
        IServiceIdProvider serviceIdProvider,
        IOptions<ColdEventStoreOptions> options,
        ILogger<HybridEventStore> logger,
        RetainedColdSegmentHolder retainedSegment,
        TimeProvider timeProvider)
    {
        _retainedSegment = retainedSegment;
        _timeProvider = timeProvider;
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

    public Task<IEnumerable<TagStream>> ReadTagsAsync(ITag tag)
        => _hotStore.ReadTagsAsync(tag);

    public Task<TagState> GetLatestTagAsync(ITag tag)
        => _hotStore.GetLatestTagAsync(tag);

    public Task<bool> TagExistsAsync(ITag tag)
        => _hotStore.TagExistsAsync(tag);

    public Task<long> GetEventCountAsync(SortableUniqueId? since = null)
        => _hotStore.GetEventCountAsync(since);

    public Task<IEnumerable<TagInfo>> GetAllTagsAsync(string? tagGroup = null)
        => _hotStore.GetAllTagsAsync(tagGroup);

    public Task<string> GetLatestSortableUniqueIdAsync()
        => _hotStore.GetLatestSortableUniqueIdAsync();

    public Task<IEnumerable<SerializableEvent>> ReadSerializableEventsByTagAsync(
        ITag tag, SortableUniqueId? since = null)
        => _hotStore.ReadSerializableEventsByTagAsync(tag, since);

    /// <summary>
    ///     Forwards only a verified native tagged stream from the hot store. In particular, this never calls the hot
    ///     list API as a pretend stream: unsupported configurations fail deterministically before any read.
    /// </summary>
    public Task<SerializableEventStreamReadResult> StreamSerializableEventsByTagAsync(
        ITag tag,
        SortableUniqueId? since,
        SortableUniqueId? until,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken = default)
    {
        var hot = SekibanDcbCapabilityResolver.ResolveTaggedStream(_hotStore, "hot event store");
        if (hot.IsSupported)
        {
            return hot.StreamStore!.StreamSerializableEventsByTagAsync(
                tag,
                since,
                until,
                onEvent,
                cancellationToken);
        }

        return Task.FromException<SerializableEventStreamReadResult>(
            new NotSupportedException(
                $"Tagged streaming is not supported by HybridEventStore({hot.Descriptor.ProviderName}): " +
                hot.UnsupportedReason));
    }

    public Task<SerializableEvent?> ReadSerializableEventAsync(Guid eventId)
        => _hotStore.ReadSerializableEventAsync(eventId);

    public Task<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>
        WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events)
        => _hotStore.WriteSerializableEventsAsync(events);

    public Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(
        SortableUniqueId? since = null)
        => ReadAllSerializableEventsAsync(since, maxCount: null);

    public async Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount)
    {
        _retainedSegment.Get(_timeProvider.GetUtcNow());
        if (!_options.Enabled)
        {
            var context = StartHybridReadLogContext(_serviceIdProvider.GetCurrentServiceId(), since, maxCount);
            return await ReadHotOnlyAsync(context, "hot_only_cold_disabled", coldBoundary: null, segmentCount: 0);
        }

        return await ReadHybridSerializableEventsAsync(since, maxCount);
    }

    public async Task<SerializableEventStreamReadResult> StreamAllSerializableEventsAsync(
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

    private async Task<IEnumerable<SerializableEvent>> ReadHybridSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount)
    {
        var serviceId = _serviceIdProvider.GetCurrentServiceId();
        var context = StartHybridReadLogContext(serviceId, since, maxCount);
        var retained = _retainedSegment.Get(_timeProvider.GetUtcNow());
        if (maxCount is > 0 && CanUseRetained(retained, serviceId, since))
        {
            var selection = SelectRetained(retained!, since!, maxCount);
            var events = selection.Events;
            if (events.Count == maxCount.Value)
            {
                UseRetained(retained!, selection.HasMore);
                LogHybridReadOutcome(context, new HybridReadOutcome(
                    "cold_only", events.Count, 0, retained!.ColdBoundary, retained.SegmentCount, false, true));
                return events;
            }
        }
        var manifest = await ColdControlFileHelper.LoadManifestAsync(_coldStorage, serviceId, CancellationToken.None);
        if (retained?.ServiceId == serviceId &&
            (manifest is null || !manifest.Segments.Contains(retained.Entry)))
            _retainedSegment.Clear(retained);

        if (manifest is null || manifest.LatestSafeSortableUniqueId is null)
        {
            _logger.LogDebug("No cold manifest found for {ServiceId}, falling back to hot store", context.ServiceId);
            return await ReadHotOnlyAsync(context, "hot_only_no_manifest", coldBoundary: null, segmentCount: 0);
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
                context.ServiceId,
                context.Since!.Value,
                manifest.LatestSafeSortableUniqueId);
            return await ReadHotOnlyAsync(
                context,
                "hot_only_since_after_cold_boundary",
                coldBoundary.Value,
                manifest.Segments.Count);
        }

        return await ReadColdAndHotEventsAsync(context, manifest, coldBoundary);
    }

    private async Task<SerializableEventStreamReadResult> StreamHybridSerializableEventsAsync(
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

    /// <summary>
    ///     Reads the hot store. A failed read is logged as a failed hybrid read with <paramref name="failureOutcome" />
    ///     and rethrown unchanged.
    /// </summary>
    private async Task<IEnumerable<SerializableEvent>> ReadHotOrLogFailureAsync(
        HybridReadLogContext context,
        SortableUniqueId? since,
        int? maxCount,
        HybridReadOutcome failureOutcome)
    {
        try
        {
            return await _hotStore.ReadAllSerializableEventsAsync(since, maxCount);
        }
        catch
        {
            LogHybridReadOutcome(context, failureOutcome);
            throw;
        }
    }

    private async Task<IEnumerable<SerializableEvent>> ReadHotOnlyAsync(
        HybridReadLogContext context,
        string source,
        string? coldBoundary,
        int segmentCount)
    {
        var hotEvents = await ReadHotOrLogFailureAsync(
            context,
            context.Since,
            context.MaxCount,
            new HybridReadOutcome(source, 0, null, coldBoundary, segmentCount, false, false));
        LogHybridReadOutcome(
            context,
            new HybridReadOutcome(
                Source: source,
                ColdEventsRead: 0,
                HotEventsRead: TryGetEventCountWithoutEnumeration(hotEvents),
                ColdBoundary: coldBoundary,
                SegmentCount: segmentCount,
                ReachedColdSegmentBoundary: false,
                Succeeded: true));
        return hotEvents;
    }

    private async Task<SerializableEventStreamReadResult> StreamHotOnlyAsync(
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

        var hotEvents = await ReadHotOrLogFailureAsync(
            context,
            since,
            maxCount,
            new HybridReadOutcome(source, 0, null, coldBoundary, segmentCount, false, false));

        var streamResult = await StreamEnumerableAsync(
            hotEvents,
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
        return streamResult;
    }

    private async Task<IEnumerable<SerializableEvent>> ReadColdAndHotEventsAsync(
        HybridReadLogContext context,
        ColdManifest manifest,
        SortableUniqueId coldBoundary)
    {
        var events = new List<SerializableEvent>(context.MaxCount.GetValueOrDefault());
        var alignToSegmentBoundary = ShouldAlignCatchUpReadsToSegmentBoundary();
        ColdReadResult coldRead;
        try
        {
            coldRead = await ReadFromColdSegmentsAsync(
                context.ServiceId,
                manifest,
                context.Since,
                context.MaxCount,
                events,
                alignToSegmentBoundary);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Cold read failed for {ServiceId}, falling back to hot store", context.ServiceId);
            return await ReadHotOnlyAsync(
                context,
                "hot_only_cold_read_failed",
                coldBoundary.Value,
                manifest.Segments.Count);
        }

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
            return events;
        }

        var hotRead = await ReadHotOrLogFailureAsync(
            context,
            coldBoundary,
            remainingCount,
            new HybridReadOutcome(
                Source: coldEventsRead > 0 ? "cold_then_hot_failed" : "hot_only_hot_failed",
                ColdEventsRead: coldEventsRead,
                HotEventsRead: 0,
                ColdBoundary: coldBoundary.Value,
                SegmentCount: manifest.Segments.Count,
                ReachedColdSegmentBoundary: coldRead.ReachedColdSegmentBoundary,
                Succeeded: false));
        var hotEvents = hotRead.ToList();
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
        return events;
    }

    private async Task<SerializableEventStreamReadResult> StreamColdAndHotEventsAsync(
        HybridReadLogContext context,
        ColdManifest manifest,
        SortableUniqueId coldBoundary,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken)
    {
        var alignToSegmentBoundary = ShouldAlignCatchUpReadsToSegmentBoundary();
        ColdStreamReadResult coldRead;
        try
        {
            coldRead = await StreamFromColdSegmentsAsync(
                manifest,
                context.Since,
                context.MaxCount,
                onEvent,
                cancellationToken,
                alignToSegmentBoundary);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Cold read failed for {ServiceId}, falling back to hot store", context.ServiceId);
            return await StreamHotOnlyAsync(
                context,
                context.Since,
                context.MaxCount,
                onEvent,
                cancellationToken,
                source: "hot_only_cold_read_failed",
                coldBoundary: coldBoundary.Value,
                segmentCount: manifest.Segments.Count);
        }

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
            return new SerializableEventStreamReadResult(
                coldRead.EventsRead,
                coldRead.LastSortableUniqueId);
        }

        var hotEvents = await ReadHotOrLogFailureAsync(
            context,
            coldBoundary,
            remainingCount,
            new HybridReadOutcome(
                Source: coldRead.EventsRead > 0 ? "cold_then_hot_failed" : "hot_only_hot_failed",
                ColdEventsRead: coldRead.EventsRead,
                HotEventsRead: 0,
                ColdBoundary: coldBoundary.Value,
                SegmentCount: manifest.Segments.Count,
                ReachedColdSegmentBoundary: coldRead.ReachedColdSegmentBoundary,
                Succeeded: false));

        var hotStreamResult = await StreamEnumerableAsync(
            hotEvents,
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
        return new SerializableEventStreamReadResult(
            totalEventsRead,
            hotStreamResult.LastSortableUniqueId ?? coldRead.LastSortableUniqueId);
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

    private static bool CanUseRetained(RetainedColdSegment? retained, string serviceId, SortableUniqueId? since)
        => retained is not null && retained.ServiceId == serviceId && since is not null
           && string.Compare(since.Value, retained.LoadedFrom, StringComparison.Ordinal) >= 0
           && string.Compare(since.Value, retained.Entry.ToSortableUniqueId, StringComparison.Ordinal) < 0;

    private static (List<SerializableEvent> Events, bool HasMore) SelectRetained(
        RetainedColdSegment retained, SortableUniqueId since, int? count)
    {
        if (!retained.IsSorted)
        {
            var eligible = retained.Events.Where(evt =>
                string.Compare(evt.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0).ToList();
            return (count.HasValue ? eligible.Take(count.Value).ToList() : eligible,
                count.HasValue && eligible.Count > count.Value);
        }

        // Upper bound excludes every equal id, including duplicates at batch boundaries.
        var low = 0;
        var high = retained.Events.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (string.Compare(retained.Events[middle].SortableUniqueIdValue, since.Value, StringComparison.Ordinal) <= 0)
                low = middle + 1;
            else
                high = middle;
        }
        var length = Math.Min(count ?? int.MaxValue, retained.Events.Count - low);
        var events = new List<SerializableEvent>(length);
        for (var i = low; i < low + length; i++) events.Add(retained.Events[i]);
        return (events, low + length < retained.Events.Count);
    }

    private static bool IsSorted(IReadOnlyList<SerializableEvent> events)
    {
        for (var i = 1; i < events.Count; i++)
            if (string.Compare(events[i - 1].SortableUniqueIdValue, events[i].SortableUniqueIdValue, StringComparison.Ordinal) > 0)
                return false;
        return true;
    }

    private void UseRetained(RetainedColdSegment retained, bool hasMore)
    {
        // Do not trim or advance loadedFrom. Another reader may still need the same suffix.
        if (!hasMore)
            _retainedSegment.Clear(retained);
        else
            _retainedSegment.Touch(retained, _timeProvider.GetUtcNow());
    }

    private async Task<ColdReadResult> ReadFromColdSegmentsAsync(
        string serviceId,
        ColdManifest manifest,
        SortableUniqueId? since,
        int? maxCount,
        List<SerializableEvent> destination,
        bool alignToSegmentBoundary)
    {
        foreach (var segment in manifest.Segments.OrderBy(s => s.FromSortableUniqueId, StringComparer.Ordinal))
        {
            var retained = _retainedSegment.Get(_timeProvider.GetUtcNow());
            if (ShouldSkipColdSegment(segment, since))
            {
                if (retained?.ServiceId == serviceId && retained.Entry == segment)
                    _retainedSegment.Clear(retained);
                continue;
            }

            var remainingCount = CalculateRemainingCount(maxCount, destination.Count);
            if (remainingCount == 0)
                return CreateColdReadResult(destination.Count, reachedColdSegmentBoundary: false);

            List<SerializableEvent> parsed;
            if (CanUseRetained(retained, serviceId, since) && retained!.Entry == segment)
            {
                var selection = SelectRetained(retained, since!, remainingCount);
                parsed = selection.Events;
                UseRetained(retained, selection.HasMore);
            }
            else
            {
                parsed = new List<SerializableEvent>();
                // Parse once to the end, including validation of lines beyond this batch.
                try
                {
                    await StreamColdSegmentAsync(segment.Path, since, null,
                        evt => { parsed.Add(evt); return ValueTask.CompletedTask; }, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _retainedSegment.Clear(retained);
                    _logger.LogWarning(ex, "Failed to parse cold segment {Path}", segment.Path);
                    throw;
                }

                if (remainingCount is > 0 && parsed.Count > remainingCount.Value)
                {
                    var loadedFrom = parsed[remainingCount.Value - 1].SortableUniqueIdValue;
                    // An unsorted returned prefix with ids above loadedFrom would be eligible again on a retry.
                    // A suffix alone cannot reproduce that read, so do not retain it.
                    if (parsed.Take(remainingCount.Value).All(evt =>
                            string.Compare(evt.SortableUniqueIdValue, loadedFrom, StringComparison.Ordinal) <= 0))
                    {
                        var suffix = parsed.Skip(remainingCount.Value).ToArray();
                        _retainedSegment.Replace(new RetainedColdSegment(serviceId, segment, loadedFrom,
                            suffix, IsSorted(suffix), manifest.Segments.Count,
                            manifest.LatestSafeSortableUniqueId!, _timeProvider.GetUtcNow()));
                    }
                    else
                        _retainedSegment.Clear(retained);
                }
                else if (retained?.ServiceId == serviceId && retained.Entry.Path == segment.Path)
                    _retainedSegment.Clear(retained);
            }

            var returned = remainingCount.HasValue ? parsed.Take(remainingCount.Value).ToList() : parsed;
            destination.AddRange(returned);
            if (returned.Count == 0) continue;
            if (ShouldStopReadingColdSegments(alignToSegmentBoundary, maxCount, destination.Count))
                return CreateColdReadResult(destination.Count,
                    !remainingCount.HasValue || parsed.Count < remainingCount.Value);
        }

        return CreateColdReadResult(destination.Count, reachedColdSegmentBoundary: false);
    }

    private sealed record ColdReadResult(
        int ColdEventsRead,
        bool ReachedColdSegmentBoundary);

    private sealed record ColdStreamReadResult(
        int EventsRead,
        string? LastSortableUniqueId,
        bool ReachedColdSegmentBoundary);

    private async Task<ColdStreamReadResult> StreamFromColdSegmentsAsync(
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
            if (ShouldSkipColdSegment(segment, since))
            {
                continue;
            }

            var remainingCount = CalculateRemainingCount(maxCount, totalEventsRead);
            if (remainingCount == 0)
            {
                return new ColdStreamReadResult(
                    totalEventsRead,
                    lastSortableUniqueId,
                    ReachedColdSegmentBoundary: false);
            }

            ColdSegmentStreamResult appendResult;
            try
            {
                appendResult = await StreamColdSegmentAsync(
                    segment.Path,
                    since,
                    remainingCount,
                    onEvent,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to parse cold segment {Path}",
                    segment.Path);
                throw;
            }

            if (appendResult.EventsRead == 0)
            {
                continue;
            }

            totalEventsRead += appendResult.EventsRead;
            lastSortableUniqueId = appendResult.LastSortableUniqueId ?? lastSortableUniqueId;
            if ((maxCount.HasValue && totalEventsRead >= maxCount.Value) || alignToSegmentBoundary)
            {
                return new ColdStreamReadResult(
                    totalEventsRead,
                    lastSortableUniqueId,
                    appendResult.ReachedEndOfSegment);
            }
        }

        return new ColdStreamReadResult(
            totalEventsRead,
            lastSortableUniqueId,
            ReachedColdSegmentBoundary: false);
    }

    private async Task<ColdSegmentStreamResult> StreamColdSegmentAsync(
        string path,
        SortableUniqueId? since,
        int? maxCount,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken)
    {
        Stream stream;
        try
        {
            stream = await _coldStorage.OpenReadAsync(path, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Failed to read cold segment {Path}", path);
            throw;
        }

        await using var _ = stream;
        return await _segmentFormatHandler.StreamSegmentAsync(
            stream,
            since,
            maxCount,
            onEvent,
            cancellationToken);
    }

    private static int? CalculateRemainingCount(int? maxCount, int currentCount)
        => maxCount.HasValue
            ? Math.Max(maxCount.Value - currentCount, 0)
            : null;

    private static ColdReadResult CreateColdReadResult(int eventCount, bool reachedColdSegmentBoundary)
        => new(eventCount, reachedColdSegmentBoundary);

    private static bool ShouldSkipColdSegment(ColdSegmentInfo segment, SortableUniqueId? since)
        => since is not null
           && string.Compare(segment.ToSortableUniqueId, since.Value, StringComparison.Ordinal) <= 0;

    private static bool ShouldStopReadingColdSegments(
        bool alignToSegmentBoundary,
        int? maxCount,
        int currentCount)
        => alignToSegmentBoundary
           || (maxCount.HasValue && currentCount >= maxCount.Value);

}
