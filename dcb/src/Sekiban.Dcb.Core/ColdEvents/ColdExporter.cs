using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.ColdEvents;

public sealed class ColdExporter : IColdEventExporter, IColdEventProgressReader
{
    private const int MaxManifestRetries = 3;
    private const string InitialManifestVersion = "0";
    private static readonly TimeSpan MaxLeaseDuration = TimeSpan.FromMinutes(2);
    private const string ReasonLeaseNotAcquired = "lease_not_acquired";
    private const string ReasonLeaseAcquireFailed = "lease_acquire_failed";
    private const string ReasonNoEventsSinceCheckpoint = "no_events_since_checkpoint";
    private const string ReasonNoSafeEvents = "no_safe_events_in_window";
    private const string ReasonExported = "exported";

    private readonly IEventStore _hotStore;
    private readonly IColdObjectStorage _storage;
    private readonly IColdLeaseManager _leaseManager;
    private readonly ColdEventStoreOptions _options;
    private readonly ILogger<ColdExporter> _logger;

    public ColdExporter(
        IEventStore hotStore,
        IColdObjectStorage storage,
        IColdLeaseManager leaseManager,
        IOptions<ColdEventStoreOptions> options,
        ILogger<ColdExporter> logger)
    {
        _hotStore = hotStore;
        _storage = storage;
        _leaseManager = leaseManager;
        _options = options.Value;
        _logger = logger;
    }

    public Task<ColdFeatureStatus> GetStatusAsync(CancellationToken ct)
        => Task.FromResult(new ColdFeatureStatus(
            IsSupported: true,
            IsEnabled: _options.Enabled,
            Reason: _options.Enabled ? "Cold event store is active" : "Cold event store is disabled"));

    public async Task<ResultBox<ColdStoreProgress>> GetProgressAsync(string serviceId, CancellationToken ct)
    {
        var manifest = await ColdControlFileHelper.LoadManifestAsync(_storage, serviceId, ct);
        var checkpoint = await ColdControlFileHelper.LoadCheckpointAsync(_storage, serviceId, ct);

        return ResultBox.FromValue(new ColdStoreProgress(
            ServiceId: serviceId,
            LatestSafeSortableUniqueId: manifest?.LatestSafeSortableUniqueId,
            LatestExportedSortableUniqueId: GetLatestExportedId(manifest),
            NextSinceSortableUniqueId: checkpoint?.NextSinceSortableUniqueId,
            LastExportedAtUtc: manifest?.UpdatedAtUtc,
            ManifestVersion: manifest?.ManifestVersion ?? InitialManifestVersion));
    }

    public async Task<ResultBox<ExportResult>> ExportIncrementalAsync(string serviceId, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            return ResultBox.Error<ExportResult>(
                new InvalidOperationException("Cold event store is disabled"));
        }

        // Keep lease short so stale leases do not block manual exports for the full pull interval.
        var leaseDuration = _options.PullInterval < MaxLeaseDuration ? _options.PullInterval : MaxLeaseDuration;
        var leaseResult = await _leaseManager.AcquireAsync(
            $"cold-export-{serviceId}", leaseDuration, ct);
        if (!leaseResult.IsSuccess)
        {
            var leaseException = leaseResult.GetException();
            if (leaseException is OperationCanceledException || ct.IsCancellationRequested)
            {
                return ResultBox.Error<ExportResult>(
                    leaseException is OperationCanceledException
                        ? leaseException
                        : new OperationCanceledException(ct));
            }

            var leaseHeld = leaseException is InvalidOperationException &&
                            leaseException.Message.Contains("already held", StringComparison.OrdinalIgnoreCase);
            if (leaseHeld)
            {
                _logger.LogInformation("Skipping export for {ServiceId}: lease not acquired ({Message})",
                    serviceId, leaseException.Message);
            }
            else
            {
                _logger.LogWarning(leaseException,
                    "Skipping export for {ServiceId}: failed to acquire lease due to unexpected error",
                    serviceId);
            }
            return ResultBox.FromValue(new ExportResult(
                ExportedEventCount: 0,
                NewSegments: [],
                UpdatedManifestVersion: InitialManifestVersion,
                Reason: leaseHeld ? ReasonLeaseNotAcquired : ReasonLeaseAcquireFailed,
                ReasonDetail: leaseException.Message));
        }

        var lease = leaseResult.GetValue();

        try
        {
            return await ExecuteExportAsync(serviceId, lease, ct);
        }
        finally
        {
            // Cleanup should run even when the request is canceled.
            var releaseResult = await _leaseManager.ReleaseAsync(lease, CancellationToken.None);
            if (!releaseResult.IsSuccess)
            {
                _logger.LogWarning(
                    releaseResult.GetException(),
                    "Failed to release cold export lease for {ServiceId} (LeaseId: {LeaseId}). Trying to expire lease immediately.",
                    serviceId,
                    lease.LeaseId);

                // Best-effort fallback: keep ownership but force expiration now to avoid long lock stalls.
                var expireResult = await _leaseManager.RenewAsync(
                    lease,
                    TimeSpan.Zero,
                    CancellationToken.None);

                if (!expireResult.IsSuccess)
                {
                    _logger.LogWarning(
                        expireResult.GetException(),
                        "Failed to force-expire cold export lease for {ServiceId} (LeaseId: {LeaseId}).",
                        serviceId,
                        lease.LeaseId);
                }
            }
        }
    }

    private async Task<ResultBox<ExportResult>> ExecuteExportAsync(
        string serviceId,
        ColdLease lease,
        CancellationToken ct)
    {
        var checkpoint = await ColdControlFileHelper.LoadCheckpointAsync(_storage, serviceId, ct);
        var since = checkpoint?.NextSinceSortableUniqueId is not null
            ? new SortableUniqueId(checkpoint.NextSinceSortableUniqueId)
            : (SortableUniqueId?)null;

        var readResult = await ReadExportCandidatesAsync(since);
        if (!readResult.IsSuccess)
        {
            return ResultBox.Error<ExportResult>(readResult.GetException());
        }

        var allEvents = readResult.GetValue().ToList();
        if (allEvents.Count == 0)
        {
            return ResultBox.FromValue(new ExportResult(
                ExportedEventCount: 0,
                NewSegments: [],
                UpdatedManifestVersion: InitialManifestVersion,
                Reason: ReasonNoEventsSinceCheckpoint));
        }

        var cutoff = DateTime.UtcNow - _options.SafeWindow;
        var safeEvents = SafeWindowFilter.Apply(allEvents, cutoff);
        if (safeEvents.Count == 0)
        {
            return ResultBox.FromValue(new ExportResult(
                ExportedEventCount: 0,
                NewSegments: [],
                UpdatedManifestVersion: InitialManifestVersion,
                Reason: ReasonNoSafeEvents));
        }

        return await UploadAndUpdateManifestAsync(serviceId, safeEvents, ct);
    }

    private async Task<ResultBox<ExportResult>> UploadAndUpdateManifestAsync(
        string serviceId,
        IReadOnlyList<SerializableEvent> allSafeEvents,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxManifestRetries; attempt++)
        {
            var updateResult = await TryUpdateManifestAndCheckpointAsync(serviceId, allSafeEvents, ct);

            if (updateResult.IsSuccess)
            {
                return updateResult;
            }

            _logger.LogWarning(
                "Manifest update conflict for {ServiceId}, attempt {Attempt}/{Max}",
                serviceId, attempt + 1, MaxManifestRetries);
        }

        return ResultBox.Error<ExportResult>(
            new InvalidOperationException($"Manifest update failed after {MaxManifestRetries} retries"));
    }

    private async Task<ResultBox<ExportResult>> TryUpdateManifestAndCheckpointAsync(
        string serviceId,
        IReadOnlyList<SerializableEvent> allSafeEvents,
        CancellationToken ct)
    {
        var existingManifest = await ColdControlFileHelper.LoadManifestWithETagAsync(_storage, serviceId, ct);
        var existingCheckpoint = await ColdControlFileHelper.LoadCheckpointWithETagAsync(_storage, serviceId, ct);
        var existingSegments = (existingManifest?.Manifest?.Segments ?? []).ToList();

        var buildResult = await BuildSegmentWritePlanAsync(serviceId, existingSegments, allSafeEvents, ct);
        if (!buildResult.IsSuccess)
        {
            return ResultBox.Error<ExportResult>(buildResult.GetException());
        }

        var plan = buildResult.GetValue();
        foreach (var write in plan.Writes)
        {
            var putResult = await _storage.PutAsync(write.Path, write.Data, write.ExpectedETag, ct);
            if (!putResult.IsSuccess)
            {
                return ResultBox.Error<ExportResult>(putResult.GetException());
            }
        }

        var lastSafe = allSafeEvents[^1].SortableUniqueIdValue;
        var newVersion = Guid.NewGuid().ToString();
        var manifest = new ColdManifest(
            ServiceId: serviceId,
            ManifestVersion: newVersion,
            LatestSafeSortableUniqueId: lastSafe,
            Segments: plan.UpdatedSegments,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        var manifestData = JsonSerializer.SerializeToUtf8Bytes(manifest, ColdEventJsonOptions.Default);
        var manifestPath = ColdStoragePaths.ManifestPath(serviceId);
        var manifestResult = await _storage.PutAsync(
            manifestPath, manifestData, existingManifest?.ETag, ct);

        if (!manifestResult.IsSuccess)
        {
            return ResultBox.Error<ExportResult>(manifestResult.GetException());
        }

        var checkpointObj = new ColdCheckpoint(
            ServiceId: serviceId,
            NextSinceSortableUniqueId: lastSafe,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        var checkpointData = JsonSerializer.SerializeToUtf8Bytes(checkpointObj, ColdEventJsonOptions.Default);
        var checkpointPath = ColdStoragePaths.CheckpointPath(serviceId);
        var checkpointResult = await _storage.PutAsync(
            checkpointPath,
            checkpointData,
            expectedETag: existingCheckpoint?.ETag,
            ct);
        if (!checkpointResult.IsSuccess)
        {
            return ResultBox.Error<ExportResult>(checkpointResult.GetException());
        }

        return ResultBox.FromValue(new ExportResult(
            ExportedEventCount: allSafeEvents.Count,
            NewSegments: plan.WrittenSegments,
            UpdatedManifestVersion: newVersion,
            Reason: ReasonExported));
    }

    private async Task<ResultBox<SegmentWritePlan>> BuildSegmentWritePlanAsync(
        string serviceId,
        List<ColdSegmentInfo> existingSegments,
        IReadOnlyList<SerializableEvent> allSafeEvents,
        CancellationToken ct)
    {
        var writes = new List<SegmentWrite>();
        var writtenSegments = new List<ColdSegmentInfo>();
        var eventsConsumed = 0;

        if (existingSegments.Count > 0)
        {
            var appendResult = await AppendToLatestSegmentIfPossibleAsync(
                existingSegments,
                allSafeEvents,
                writes,
                writtenSegments,
                ct);
            if (!appendResult.IsSuccess)
            {
                return ResultBox.Error<SegmentWritePlan>(appendResult.GetException());
            }

            eventsConsumed = appendResult.GetValue();
        }

        var remaining = allSafeEvents.Skip(eventsConsumed).ToList();
        if (remaining.Count > 0)
        {
            var newSegments = ColdSegmentSplitter.Split(remaining, _options.SegmentMaxEvents, _options.SegmentMaxBytes);
            foreach (var segment in newSegments)
            {
                var segmentData = JsonlSegmentWriter.Write(segment);
                var segmentPath = ColdStoragePaths.SegmentPath(
                    serviceId, segment[0].SortableUniqueIdValue, segment[^1].SortableUniqueIdValue);
                var segmentInfo = new ColdSegmentInfo(
                    Path: segmentPath,
                    FromSortableUniqueId: segment[0].SortableUniqueIdValue,
                    ToSortableUniqueId: segment[^1].SortableUniqueIdValue,
                    EventCount: segment.Count,
                    SizeBytes: segmentData.Length,
                    Sha256: ComputeSha256(segmentData),
                    CreatedAtUtc: DateTimeOffset.UtcNow);

                existingSegments.Add(segmentInfo);
                writes.Add(new SegmentWrite(segmentPath, segmentData, ExpectedETag: null));
                writtenSegments.Add(segmentInfo);
            }
        }

        return ResultBox.FromValue(new SegmentWritePlan(existingSegments, writes, writtenSegments));
    }

    private async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadExportCandidatesAsync(SortableUniqueId? since)
    {
        var limit = _options.ExportMaxEventsPerRun;
        if (limit > 0)
        {
            var limited = await _hotStore.ReadAllSerializableEventsAsync(since, limit);
            if (limited.IsSuccess)
            {
                return limited;
            }

            var ex = limited.GetException();
            if (ex is not NotSupportedException)
            {
                return ResultBox.Error<IEnumerable<SerializableEvent>>(ex);
            }

            _logger.LogDebug(
                "Falling back to unbounded cold export read because max-count overload is not supported by {StoreType}",
                _hotStore.GetType().Name);
        }

        return await _hotStore.ReadAllSerializableEventsAsync(since);
    }

    private async Task<ResultBox<int>> AppendToLatestSegmentIfPossibleAsync(
        List<ColdSegmentInfo> existingSegments,
        IReadOnlyList<SerializableEvent> allSafeEvents,
        List<SegmentWrite> writes,
        List<ColdSegmentInfo> writtenSegments,
        CancellationToken ct)
    {
        var lastSegment = existingSegments[^1];
        var lastSegmentObject = await _storage.GetAsync(lastSegment.Path, ct);
        if (!lastSegmentObject.IsSuccess)
        {
            return ResultBox.Error<int>(lastSegmentObject.GetException());
        }

        var existingEventsResult = ParseSegmentEvents(lastSegmentObject.GetValue().Data);
        if (!existingEventsResult.IsSuccess)
        {
            return ResultBox.Error<int>(existingEventsResult.GetException());
        }

        var existingEvents = existingEventsResult.GetValue();
        var appendCount = CountAppendableEvents(lastSegment, existingEvents, allSafeEvents);
        if (appendCount <= 0)
        {
            return ResultBox.FromValue(0);
        }

        var appended = allSafeEvents.Take(appendCount).ToList();
        var mergedEvents = existingEvents.Concat(appended).ToList();
        var mergedData = JsonlSegmentWriter.Write(mergedEvents);
        var updatedLast = new ColdSegmentInfo(
            Path: lastSegment.Path,
            FromSortableUniqueId: lastSegment.FromSortableUniqueId,
            ToSortableUniqueId: mergedEvents[^1].SortableUniqueIdValue,
            EventCount: mergedEvents.Count,
            SizeBytes: mergedData.Length,
            Sha256: ComputeSha256(mergedData),
            CreatedAtUtc: lastSegment.CreatedAtUtc);

        existingSegments[^1] = updatedLast;
        writes.Add(new SegmentWrite(lastSegment.Path, mergedData, lastSegmentObject.GetValue().ETag));
        writtenSegments.Add(updatedLast);
        return ResultBox.FromValue(appendCount);
    }

    private int CountAppendableEvents(
        ColdSegmentInfo lastSegment,
        IReadOnlyList<SerializableEvent> existingEvents,
        IReadOnlyList<SerializableEvent> incomingEvents)
    {
        var payloadBytes = existingEvents.Sum(e => (long)e.Payload.Length);
        var appendCount = 0;

        while (appendCount < incomingEvents.Count
               && lastSegment.EventCount + appendCount < _options.SegmentMaxEvents)
        {
            var nextPayloadBytes = payloadBytes + incomingEvents[appendCount].Payload.Length;
            if (nextPayloadBytes > _options.SegmentMaxBytes)
            {
                break;
            }

            payloadBytes = nextPayloadBytes;
            appendCount++;
        }

        return appendCount;
    }

    private static ResultBox<IReadOnlyList<SerializableEvent>> ParseSegmentEvents(byte[] data)
    {
        try
        {
            var text = System.Text.Encoding.UTF8.GetString(data);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var events = new List<SerializableEvent>(lines.Length);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var evt = JsonSerializer.Deserialize<SerializableEvent>(line, ColdEventJsonOptions.Default);
                if (evt is null)
                {
                    return ResultBox.Error<IReadOnlyList<SerializableEvent>>(
                        new InvalidDataException("Failed to deserialize cold segment line."));
                }

                events.Add(evt);
            }

            return ResultBox.FromValue<IReadOnlyList<SerializableEvent>>(events);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IReadOnlyList<SerializableEvent>>(ex);
        }
    }

    private static string? GetLatestExportedId(ColdManifest? manifest)
    {
        if (manifest is null || manifest.Segments.Count == 0)
        {
            return null;
        }
        return manifest.Segments[^1].ToSortableUniqueId;
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    private sealed record SegmentWrite(string Path, byte[] Data, string? ExpectedETag);
    private sealed record SegmentWritePlan(
        IReadOnlyList<ColdSegmentInfo> UpdatedSegments,
        IReadOnlyList<SegmentWrite> Writes,
        IReadOnlyList<ColdSegmentInfo> WrittenSegments);
}
