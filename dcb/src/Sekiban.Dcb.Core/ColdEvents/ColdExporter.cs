using System.Runtime.CompilerServices;
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

    private readonly IHotEventStore _hotStore;
    private readonly IColdObjectStorage _storage;
    private readonly IColdSegmentFormatHandler _segmentFormatHandler;
    private readonly IColdLeaseManager _leaseManager;
    private readonly ColdEventStoreOptions _options;
    private readonly ILogger<ColdExporter> _logger;

    public ColdExporter(
        IHotEventStore hotStore,
        IColdObjectStorage storage,
        IColdSegmentFormatHandler segmentFormatHandler,
        IColdLeaseManager leaseManager,
        IOptions<ColdEventStoreOptions> options,
        ILogger<ColdExporter> logger)
    {
        _hotStore = hotStore;
        _storage = storage;
        _segmentFormatHandler = segmentFormatHandler;
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
            return await ExecuteExportAsync(serviceId, ct);
        }
        finally
        {
            var releaseResult = await _leaseManager.ReleaseAsync(lease, CancellationToken.None);
            if (!releaseResult.IsSuccess)
            {
                _logger.LogWarning(
                    releaseResult.GetException(),
                    "Failed to release cold export lease for {ServiceId} (LeaseId: {LeaseId}). Trying to expire lease immediately.",
                    serviceId,
                    lease.LeaseId);

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
        CancellationToken ct)
    {
        var checkpoint = await ColdControlFileHelper.LoadCheckpointAsync(_storage, serviceId, ct);
        var since = checkpoint?.NextSinceSortableUniqueId is not null
            ? new SortableUniqueId(checkpoint.NextSinceSortableUniqueId)
            : (SortableUniqueId?)null;

        var stageResult = await StageSegmentsAsync(serviceId, since, ct);
        if (!stageResult.IsSuccess)
        {
            return ResultBox.Error<ExportResult>(stageResult.GetException());
        }

        var stage = stageResult.GetValue();
        if (stage.ImmediateResult is not null)
        {
            return ResultBox.FromValue(stage.ImmediateResult);
        }

        try
        {
            for (var attempt = 0; attempt < MaxManifestRetries; attempt++)
            {
                var updateResult = await TryUpdateManifestAndCheckpointAsync(serviceId, stage, ct);
                if (updateResult.IsSuccess)
                {
                    return updateResult;
                }

                _logger.LogWarning(
                    "Manifest update conflict for {ServiceId}, attempt {Attempt}/{Max}",
                    serviceId, attempt + 1, MaxManifestRetries);
            }
        }
        finally
        {
            await stage.DisposeAsync();
        }

        return ResultBox.Error<ExportResult>(
            new InvalidOperationException($"Manifest update failed after {MaxManifestRetries} retries"));
    }

    private async Task<ResultBox<StagedExportResult>> StageSegmentsAsync(
        string serviceId,
        SortableUniqueId? since,
        CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - _options.SafeWindow;
        var stagedSegments = new List<StagedSegment>();
        IColdSegmentFileBuilder? currentSegment = null;
        var sawAnyEvents = false;
        var sawSafeEvents = false;
        string? lastSafeSortableUniqueId = null;

        try
        {
            await foreach (var evt in ReadExportCandidatesAsStreamAsync(since, ct).WithCancellation(ct))
            {
                sawAnyEvents = true;
                if (!IsSafe(evt, cutoff))
                {
                    break;
                }

                sawSafeEvents = true;
                lastSafeSortableUniqueId = evt.SortableUniqueIdValue;

                currentSegment = await StartOrAppendSegmentAsync(
                    currentSegment,
                    stagedSegments,
                    evt,
                    serviceId,
                    ct);
            }

            if (currentSegment is not null)
            {
                var completed = await currentSegment.CompleteAsync(serviceId, ct);
                stagedSegments.Add(new StagedSegment(completed.FilePath, completed.Info));
            }

            if (!sawAnyEvents)
            {
                return CreateImmediateStageResult(ReasonNoEventsSinceCheckpoint);
            }

            if (!sawSafeEvents || lastSafeSortableUniqueId is null)
            {
                return CreateImmediateStageResult(ReasonNoSafeEvents);
            }

            return ResultBox.FromValue(new StagedExportResult(
                ImmediateResult: null,
                Segments: stagedSegments,
                LastSafeSortableUniqueId: lastSafeSortableUniqueId));
        }
        catch (Exception ex)
        {
            await DisposeStagedResourcesAsync(stagedSegments, currentSegment);
            return ResultBox.Error<StagedExportResult>(ex);
        }
    }

    private async Task<IColdSegmentFileBuilder> StartOrAppendSegmentAsync(
        IColdSegmentFileBuilder? currentSegment,
        List<StagedSegment> stagedSegments,
        SerializableEvent evt,
        string serviceId,
        CancellationToken ct)
    {
        if (currentSegment is null)
        {
            return await _segmentFormatHandler.CreateBuilderAsync(evt, ct);
        }

        if (currentSegment.CanAppend(evt, _options))
        {
            await currentSegment.AppendAsync(evt, ct);
            return currentSegment;
        }

        var completed = await currentSegment.CompleteAsync(serviceId, ct);
        stagedSegments.Add(new StagedSegment(completed.FilePath, completed.Info));
        return await _segmentFormatHandler.CreateBuilderAsync(evt, ct);
    }

    private static ResultBox<StagedExportResult> CreateImmediateStageResult(string reason) =>
        ResultBox.FromValue(new StagedExportResult(
            new ExportResult(
                ExportedEventCount: 0,
                NewSegments: [],
                UpdatedManifestVersion: InitialManifestVersion,
                Reason: reason),
            [],
            null));

    private static async Task DisposeStagedResourcesAsync(
        IEnumerable<StagedSegment> stagedSegments,
        IColdSegmentFileBuilder? currentSegment)
    {
        foreach (var staged in stagedSegments)
        {
            await staged.DisposeAsync();
        }

        if (currentSegment is not null)
        {
            await currentSegment.DisposeAsync();
        }
    }

    private async Task<ResultBox<ExportResult>> TryUpdateManifestAndCheckpointAsync(
        string serviceId,
        StagedExportResult stage,
        CancellationToken ct)
    {
        var existingManifest = await ColdControlFileHelper.LoadManifestWithETagAsync(_storage, serviceId, ct);
        var existingCheckpoint = await ColdControlFileHelper.LoadCheckpointWithETagAsync(_storage, serviceId, ct);
        var updatedSegments = (existingManifest?.Manifest?.Segments ?? []).ToList();

        foreach (var segment in stage.Segments)
        {
            var putResult = await segment.UploadAsync(_storage, ct);
            if (!putResult.IsSuccess)
            {
                return ResultBox.Error<ExportResult>(putResult.GetException());
            }

            updatedSegments.Add(segment.Info);
        }

        var newVersion = Guid.NewGuid().ToString();
        var manifest = new ColdManifest(
            ServiceId: serviceId,
            ManifestVersion: newVersion,
            LatestSafeSortableUniqueId: stage.LastSafeSortableUniqueId!,
            Segments: updatedSegments,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        var manifestData = JsonSerializer.SerializeToUtf8Bytes(manifest, ColdEventJsonOptions.Default);
        var manifestPath = ColdStoragePaths.ManifestPath(serviceId);
        var manifestResult = await _storage.PutAsync(
            manifestPath, manifestData, existingManifest?.ETag, ct);
        if (!manifestResult.IsSuccess)
        {
            return ResultBox.Error<ExportResult>(manifestResult.GetException());
        }

        var checkpoint = new ColdCheckpoint(
            ServiceId: serviceId,
            NextSinceSortableUniqueId: stage.LastSafeSortableUniqueId!,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        var checkpointData = JsonSerializer.SerializeToUtf8Bytes(checkpoint, ColdEventJsonOptions.Default);
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
            ExportedEventCount: stage.Segments.Sum(x => x.Info.EventCount),
            NewSegments: stage.Segments.Select(x => x.Info).ToList(),
            UpdatedManifestVersion: newVersion,
            Reason: ReasonExported));
    }

    private async IAsyncEnumerable<SerializableEvent> ReadExportCandidatesAsStreamAsync(
        SortableUniqueId? since,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var limit = _options.ExportMaxEventsPerRun > 0
            ? _options.ExportMaxEventsPerRun
            : (int?)null;

        if (_hotStore is ISerializableEventStreamReader streamReader)
        {
            await foreach (var evt in streamReader.StreamAllSerializableEventsAsync(since, limit, ct)
                               .WithCancellation(ct))
            {
                yield return evt;
            }

            yield break;
        }

        var readResult = await ReadExportCandidatesAsync(since);
        if (!readResult.IsSuccess)
        {
            throw new InvalidOperationException(
                "Failed to read export candidates from event store.",
                readResult.GetException());
        }

        foreach (var evt in readResult.GetValue())
        {
            ct.ThrowIfCancellationRequested();
            yield return evt;
        }
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

    private static bool IsSafe(SerializableEvent evt, DateTime cutoffUtc)
    {
        var eventTime = new SortableUniqueId(evt.SortableUniqueIdValue).GetDateTime();
        return eventTime <= cutoffUtc;
    }

    private static string? GetLatestExportedId(ColdManifest? manifest)
    {
        if (manifest is null || manifest.Segments.Count == 0)
        {
            return null;
        }

        return manifest.Segments[^1].ToSortableUniqueId;
    }

    private sealed record StagedExportResult(
        ExportResult? ImmediateResult,
        IReadOnlyList<StagedSegment> Segments,
        string? LastSafeSortableUniqueId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            foreach (var segment in Segments)
            {
                await segment.DisposeAsync();
            }
        }
    }

    private sealed class StagedSegment : IAsyncDisposable
    {
        public StagedSegment(string filePath, ColdSegmentInfo info)
        {
            FilePath = filePath;
            Info = info;
        }

        public string FilePath { get; }
        public ColdSegmentInfo Info { get; }

        public async Task<ResultBox<bool>> UploadAsync(IColdObjectStorage storage, CancellationToken ct)
        {
            await using var stream = new FileStream(
                FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await storage.PutAsync(Info.Path, stream, expectedETag: null, ct);
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch
            {
                // Best effort cleanup.
            }

            return ValueTask.CompletedTask;
        }
    }

}
