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

        var readResult = await _hotStore.ReadAllSerializableEventsAsync(since);
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

        var segments = ColdSegmentSplitter.Split(
            safeEvents, _options.SegmentMaxEvents, _options.SegmentMaxBytes);

        return await UploadAndUpdateManifestAsync(serviceId, segments, safeEvents, ct);
    }

    private async Task<ResultBox<ExportResult>> UploadAndUpdateManifestAsync(
        string serviceId,
        IReadOnlyList<IReadOnlyList<SerializableEvent>> segments,
        IReadOnlyList<SerializableEvent> allSafeEvents,
        CancellationToken ct)
    {
        var newSegmentInfos = new List<ColdSegmentInfo>();

        foreach (var segment in segments)
        {
            var segmentData = JsonlSegmentWriter.Write(segment);
            var sha256 = ComputeSha256(segmentData);
            var segmentPath = ColdStoragePaths.SegmentPath(
                serviceId, segment[0].SortableUniqueIdValue, segment[^1].SortableUniqueIdValue);

            var putResult = await _storage.PutAsync(segmentPath, segmentData, expectedETag: null, ct);
            if (!putResult.IsSuccess)
            {
                return ResultBox.Error<ExportResult>(putResult.GetException());
            }

            newSegmentInfos.Add(new ColdSegmentInfo(
                Path: segmentPath,
                FromSortableUniqueId: segment[0].SortableUniqueIdValue,
                ToSortableUniqueId: segment[^1].SortableUniqueIdValue,
                EventCount: segment.Count,
                SizeBytes: segmentData.Length,
                Sha256: sha256,
                CreatedAtUtc: DateTimeOffset.UtcNow));
        }

        for (var attempt = 0; attempt < MaxManifestRetries; attempt++)
        {
            var updateResult = await TryUpdateManifestAndCheckpointAsync(
                serviceId, newSegmentInfos, allSafeEvents, ct);

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
        List<ColdSegmentInfo> newSegmentInfos,
        IReadOnlyList<SerializableEvent> allSafeEvents,
        CancellationToken ct)
    {
        var existingManifest = await ColdControlFileHelper.LoadManifestWithETagAsync(_storage, serviceId, ct);
        var existingCheckpoint = await ColdControlFileHelper.LoadCheckpointWithETagAsync(_storage, serviceId, ct);
        var existingSegments = existingManifest?.Manifest?.Segments ?? [];
        var allSegments = existingSegments.Concat(newSegmentInfos).ToList();

        var lastSafe = allSafeEvents[^1].SortableUniqueIdValue;
        var newVersion = Guid.NewGuid().ToString();
        var manifest = new ColdManifest(
            ServiceId: serviceId,
            ManifestVersion: newVersion,
            LatestSafeSortableUniqueId: lastSafe,
            Segments: allSegments,
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
            NewSegments: newSegmentInfos,
            UpdatedManifestVersion: newVersion,
            Reason: ReasonExported));
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
}
