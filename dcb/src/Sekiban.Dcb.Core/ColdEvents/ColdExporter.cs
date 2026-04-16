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
                ReasonDetail: leaseException.Message,
                ShouldContinueWithinCycle: false));
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
                LastSafeSortableUniqueId: lastSafeSortableUniqueId,
                ExportedEventCount: stagedSegments.Sum(x => x.Info.EventCount)));
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
                Reason: reason,
                ShouldContinueWithinCycle: false),
            [],
            null,
            0));

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
        await using var updatePlan = await BuildManifestUpdatePlanAsync(
            serviceId,
            existingManifest?.Manifest,
            stage,
            ct);
        var updatedSegments = updatePlan.UpdatedManifestSegments.ToList();
        var uploadedPaths = new List<string>();

        foreach (var segment in updatePlan.SegmentsToUpload)
        {
            var putResult = await segment.UploadAsync(_storage, ct);
            if (!putResult.IsSuccess)
            {
                await CleanupUploadedSegmentsAsync(uploadedPaths, ct);
                return ResultBox.Error<ExportResult>(putResult.GetException());
            }
            uploadedPaths.Add(segment.Info.Path);
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
            await CleanupUploadedSegmentsAsync(uploadedPaths, ct);
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

        await DeleteSegmentsAsync(updatePlan.ReplacedSegmentPaths, ct);

        return ResultBox.FromValue(new ExportResult(
            ExportedEventCount: updatePlan.AppliedEventCount,
            NewSegments: updatePlan.ResultSegments.Select(x => x.Info).ToList(),
            UpdatedManifestVersion: newVersion,
            Reason: ReasonExported,
            ShouldContinueWithinCycle: true));
    }

    private async Task<ManifestUpdatePlan> BuildManifestUpdatePlanAsync(
        string serviceId,
        ColdManifest? existingManifest,
        StagedExportResult stage,
        CancellationToken ct)
    {
        var existingSegments = (existingManifest?.Segments ?? []).ToList();
        if (existingSegments.Count == 0 || stage.Segments.Count == 0)
        {
            return ManifestUpdatePlan.ForAppend(existingSegments, stage.Segments, [], stage.ExportedEventCount);
        }

        var existingTail = existingSegments[^1];
        var adjustedStage = await RemoveTailOverlapAsync(serviceId, existingTail, stage.Segments, ct);
        var adjustedSegments = adjustedStage.Segments;
        if (adjustedSegments.Count == 0)
        {
            return new ManifestUpdatePlan(
                UpdatedManifestSegments: existingSegments,
                SegmentsToUpload: [],
                ResultSegments: [],
                ReplacedSegmentPaths: [],
                OwnedSegments: adjustedStage.OwnedSegments,
                AppliedEventCount: 0);
        }

        var firstStagedSegment = adjustedSegments[0];
        var tailMergeDecision = EvaluateTailMerge(existingTail, firstStagedSegment.Info);
        if (!tailMergeDecision.CanMerge)
        {
            LogTailMergeSkip(serviceId, tailMergeDecision);
            return ManifestUpdatePlan.ForAppend(
                existingSegments,
                adjustedSegments,
                adjustedStage.OwnedSegments,
                adjustedStage.AppliedEventCount);
        }

        var mergeResult = await TryMergeTailSegmentAsync(serviceId, existingTail, firstStagedSegment, ct);
        if (!mergeResult.IsSuccess)
        {
            _logger.LogWarning(
                mergeResult.GetException(),
                "Tail merge fallback for {ServiceId}: failed to merge staged segment into existing tail",
                serviceId);
            return ManifestUpdatePlan.ForAppend(
                existingSegments,
                adjustedSegments,
                adjustedStage.OwnedSegments,
                adjustedStage.AppliedEventCount);
        }

        var mergeAttempt = mergeResult.GetValue();
        if (!mergeAttempt.Merged)
        {
            return ManifestUpdatePlan.ForAppend(
                existingSegments,
                adjustedSegments,
                adjustedStage.OwnedSegments,
                adjustedStage.AppliedEventCount);
        }
        var mergedTail = mergeAttempt.Segment!;

        var resultSegments = new List<StagedSegment> { mergedTail };
        resultSegments.AddRange(adjustedSegments.Skip(1));

        var updatedSegments = existingSegments.Take(existingSegments.Count - 1).ToList();
        updatedSegments.AddRange(resultSegments.Select(x => x.Info));

        var replacedSegmentPaths = mergedTail.Info.Path == existingTail.Path
            ? Array.Empty<string>()
            : new[] { existingTail.Path };

        return new ManifestUpdatePlan(
            UpdatedManifestSegments: updatedSegments,
            SegmentsToUpload: resultSegments,
            ResultSegments: resultSegments,
            ReplacedSegmentPaths: replacedSegmentPaths,
            OwnedSegments: adjustedStage.OwnedSegments.Concat([mergedTail]).ToList(),
            AppliedEventCount: adjustedStage.AppliedEventCount);
    }

    private TailMergeDecision EvaluateTailMerge(
        ColdSegmentInfo existingTail,
        ColdSegmentInfo firstStagedSegment)
    {
        if (!_options.EnableTailMerge)
        {
            return new TailMergeDecision(false, TailMergeSkipReason.Disabled, null, _options.TailMergeMaxLocalBytes);
        }

        if (firstStagedSegment.EventCount <= 0
            || existingTail.EventCount >= _options.SegmentMaxEvents
            || existingTail.SizeBytes >= _options.SegmentMaxBytes
            || existingTail.EventCount + firstStagedSegment.EventCount > _options.SegmentMaxEvents
            || existingTail.SizeBytes + firstStagedSegment.SizeBytes > _options.SegmentMaxBytes)
        {
            return new TailMergeDecision(false, TailMergeSkipReason.SegmentLimits, null, _options.TailMergeMaxLocalBytes);
        }

        var estimatedLocalBytes = EstimateTailMergeLocalBytes(existingTail, firstStagedSegment);
        if (_options.TailMergeMaxLocalBytes is > 0 && estimatedLocalBytes > _options.TailMergeMaxLocalBytes.Value)
        {
            return new TailMergeDecision(
                false,
                TailMergeSkipReason.LocalBudgetExceeded,
                estimatedLocalBytes,
                _options.TailMergeMaxLocalBytes);
        }

        return new TailMergeDecision(true, null, estimatedLocalBytes, _options.TailMergeMaxLocalBytes);
    }

    private static long EstimateTailMergeLocalBytes(
        ColdSegmentInfo existingTail,
        ColdSegmentInfo firstStagedSegment)
    {
        try
        {
            return checked(existingTail.SizeBytes + firstStagedSegment.SizeBytes + firstStagedSegment.SizeBytes);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private void LogTailMergeSkip(string serviceId, TailMergeDecision decision)
    {
        if (decision.Reason is null)
        {
            return;
        }

        if (decision.Reason == TailMergeSkipReason.LocalBudgetExceeded)
        {
            _logger.LogInformation(
                "Skipping tail merge for {ServiceId}: estimated local merge size {EstimatedBytes} exceeds budget {BudgetBytes}",
                serviceId,
                decision.EstimatedLocalBytes,
                decision.LocalBudgetBytes);
            return;
        }

        if (decision.Reason == TailMergeSkipReason.Disabled)
        {
            _logger.LogInformation(
                "Skipping tail merge for {ServiceId}: tail merge is disabled by configuration",
                serviceId);
        }
    }

    private async Task<ResultBox<MergeTailAttempt>> TryMergeTailSegmentAsync(
        string serviceId,
        ColdSegmentInfo existingTail,
        StagedSegment firstStagedSegment,
        CancellationToken ct)
    {
        if (existingTail.EventCount >= _options.SegmentMaxEvents
            || existingTail.SizeBytes >= _options.SegmentMaxBytes
            || existingTail.EventCount + firstStagedSegment.Info.EventCount > _options.SegmentMaxEvents
            || existingTail.SizeBytes + firstStagedSegment.Info.SizeBytes > _options.SegmentMaxBytes)
        {
            return ResultBox.FromValue(new MergeTailAttempt(false, null));
        }

        IColdSegmentFileBuilder? builder = null;
        try
        {
            var existingEventsResult = await AppendExistingTailAsync(existingTail, ct);
            if (!existingEventsResult.IsSuccess)
            {
                return ResultBox.Error<MergeTailAttempt>(existingEventsResult.GetException());
            }

            builder = existingEventsResult.GetValue();
            var appendResult = await TryAppendLocalSegmentToBuilderAsync(firstStagedSegment, builder, ct);
            if (!appendResult.IsSuccess)
            {
                return ResultBox.Error<MergeTailAttempt>(appendResult.GetException());
            }

            if (!appendResult.GetValue())
            {
                return ResultBox.FromValue(new MergeTailAttempt(false, null));
            }

            var completed = await builder.CompleteAsync(serviceId, ct);
            builder = null;
            return ResultBox.FromValue(new MergeTailAttempt(true, new StagedSegment(completed.FilePath, completed.Info)));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<MergeTailAttempt>(ex);
        }
        finally
        {
            if (builder is not null)
            {
                await builder.DisposeAsync();
            }
        }
    }

    private async Task<TailOverlapAdjustment> RemoveTailOverlapAsync(
        string serviceId,
        ColdSegmentInfo existingTail,
        IReadOnlyList<StagedSegment> stagedSegments,
        CancellationToken ct)
    {
        if (stagedSegments.Count == 0)
        {
            return new TailOverlapAdjustment([], [], 0);
        }

        var adjustedSegments = new List<StagedSegment>();
        var ownedSegments = new List<StagedSegment>();
        var appliedEventCount = 0;
        var overlapResolved = false;

        foreach (var stagedSegment in stagedSegments)
        {
            if (overlapResolved)
            {
                adjustedSegments.Add(stagedSegment);
                appliedEventCount += stagedSegment.Info.EventCount;
                continue;
            }

            if (string.Compare(stagedSegment.Info.FromSortableUniqueId, existingTail.ToSortableUniqueId, StringComparison.Ordinal) > 0)
            {
                overlapResolved = true;
                adjustedSegments.Add(stagedSegment);
                appliedEventCount += stagedSegment.Info.EventCount;
                continue;
            }

            var stagedEvents = (await ReadEventsFromLocalSegmentAsync(stagedSegment, ct)).GetValue();
            var filteredEvents = stagedEvents
                .Where(evt => string.Compare(evt.SortableUniqueIdValue, existingTail.ToSortableUniqueId, StringComparison.Ordinal) > 0)
                .ToList();

            if (filteredEvents.Count == 0)
            {
                continue;
            }

            overlapResolved = true;
            if (filteredEvents.Count == stagedEvents.Count)
            {
                adjustedSegments.Add(stagedSegment);
                appliedEventCount += stagedSegment.Info.EventCount;
                continue;
            }

            var rebuiltSegment = await CreateSegmentFromEventsAsync(serviceId, filteredEvents, ct);
            ownedSegments.Add(rebuiltSegment);
            adjustedSegments.Add(rebuiltSegment);
            appliedEventCount += rebuiltSegment.Info.EventCount;
        }

        return new TailOverlapAdjustment(adjustedSegments, ownedSegments, appliedEventCount);
    }

    private async Task<ResultBox<IColdSegmentFileBuilder>> AppendExistingTailAsync(
        ColdSegmentInfo existingTail,
        CancellationToken ct)
    {
        IColdSegmentFileBuilder? builder = null;
        var streamResult = await _storage.OpenReadAsync(existingTail.Path, ct);
        if (!streamResult.IsSuccess)
        {
            return ResultBox.Error<IColdSegmentFileBuilder>(streamResult.GetException());
        }

        await using var stream = streamResult.GetValue();
        var readResult = await _segmentFormatHandler.StreamSegmentAsync(
            stream,
            since: null,
            maxCount: null,
            async evt =>
            {
                if (builder is null)
                {
                    builder = await _segmentFormatHandler.CreateBuilderAsync(evt, ct);
                    return;
                }

                await builder.AppendAsync(evt, ct);
            },
            ct);
        if (!readResult.IsSuccess)
        {
            if (builder is not null)
            {
                await builder.DisposeAsync();
                builder = null;
            }
            return ResultBox.Error<IColdSegmentFileBuilder>(readResult.GetException());
        }

        return builder is null
            ? ResultBox.Error<IColdSegmentFileBuilder>(
                new InvalidOperationException($"Cold segment {existingTail.Path} did not contain any events."))
            : ResultBox.FromValue<IColdSegmentFileBuilder>(builder);
    }

    private async Task<ResultBox<bool>> TryAppendLocalSegmentToBuilderAsync(
        StagedSegment stagedSegment,
        IColdSegmentFileBuilder builder,
        CancellationToken ct)
    {
        await using var stream = new FileStream(
            stagedSegment.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        try
        {
            var readResult = await _segmentFormatHandler.StreamSegmentAsync(
                stream,
                since: null,
                maxCount: null,
                async evt =>
                {
                    if (!builder.CanAppend(evt, _options))
                    {
                        throw new MergeCapacityExceededException();
                    }

                    await builder.AppendAsync(evt, ct);
                },
                ct);

            return !readResult.IsSuccess
                ? ResultBox.Error<bool>(readResult.GetException())
                : ResultBox.FromValue(true);
        }
        catch (MergeCapacityExceededException)
        {
            return ResultBox.FromValue(false);
        }
    }

    private async Task<ResultBox<List<SerializableEvent>>> ReadEventsFromLocalSegmentAsync(
        StagedSegment stagedSegment,
        CancellationToken ct)
    {
        var events = new List<SerializableEvent>(stagedSegment.Info.EventCount);
        await using var stream = new FileStream(
            stagedSegment.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var readResult = await _segmentFormatHandler.StreamSegmentAsync(
            stream,
            since: null,
            maxCount: null,
            evt =>
            {
                events.Add(evt);
                return ValueTask.CompletedTask;
            },
            ct);

        return !readResult.IsSuccess
            ? ResultBox.Error<List<SerializableEvent>>(readResult.GetException())
            : ResultBox.FromValue(events);
    }

    private async Task<StagedSegment> CreateSegmentFromEventsAsync(
        string serviceId,
        IReadOnlyList<SerializableEvent> events,
        CancellationToken ct)
    {
        if (events.Count == 0)
        {
            throw new InvalidOperationException("Cannot create a cold segment from an empty event list.");
        }

        IColdSegmentFileBuilder? builder = null;
        try
        {
            builder = await _segmentFormatHandler.CreateBuilderAsync(events[0], ct);
            for (var i = 1; i < events.Count; i++)
            {
                await builder.AppendAsync(events[i], ct);
            }

            var completed = await builder.CompleteAsync(serviceId, ct);
            builder = null;
            return new StagedSegment(completed.FilePath, completed.Info);
        }
        catch
        {
            if (builder is not null)
            {
                await builder.DisposeAsync();
            }

            throw;
        }
    }

    private async Task CleanupUploadedSegmentsAsync(
        IEnumerable<string> uploadedPaths,
        CancellationToken ct)
    {
        foreach (var path in uploadedPaths)
        {
            var deleteResult = await _storage.DeleteAsync(path, ct);
            if (!deleteResult.IsSuccess)
            {
                _logger.LogWarning(deleteResult.GetException(), "Failed to delete uploaded cold segment {Path}", path);
            }
        }
    }

    private async Task DeleteSegmentsAsync(
        IEnumerable<string> segmentPaths,
        CancellationToken ct)
    {
        foreach (var path in segmentPaths)
        {
            var deleteResult = await _storage.DeleteAsync(path, ct);
            if (!deleteResult.IsSuccess)
            {
                _logger.LogWarning(deleteResult.GetException(), "Failed to delete replaced cold segment {Path}", path);
            }
        }
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
        string? LastSafeSortableUniqueId,
        int ExportedEventCount) : IAsyncDisposable
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

    private sealed record ManifestUpdatePlan(
        IReadOnlyList<ColdSegmentInfo> UpdatedManifestSegments,
        IReadOnlyList<StagedSegment> SegmentsToUpload,
        IReadOnlyList<StagedSegment> ResultSegments,
        IReadOnlyList<string> ReplacedSegmentPaths,
        IReadOnlyList<StagedSegment> OwnedSegments,
        int AppliedEventCount) : IAsyncDisposable
    {
        public static ManifestUpdatePlan ForAppend(
            IReadOnlyList<ColdSegmentInfo> existingSegments,
            IReadOnlyList<StagedSegment> stageSegments,
            IReadOnlyList<StagedSegment> ownedSegments,
            int appliedEventCount)
            => new(
                UpdatedManifestSegments: existingSegments.Concat(stageSegments.Select(x => x.Info)).ToList(),
                SegmentsToUpload: stageSegments,
                ResultSegments: stageSegments,
                ReplacedSegmentPaths: [],
                OwnedSegments: ownedSegments,
                AppliedEventCount: appliedEventCount);

        public async ValueTask DisposeAsync()
        {
            foreach (var segment in OwnedSegments)
            {
                await segment.DisposeAsync();
            }
        }
    }

    private sealed record MergeTailAttempt(bool Merged, StagedSegment? Segment);

    private sealed record TailOverlapAdjustment(
        IReadOnlyList<StagedSegment> Segments,
        IReadOnlyList<StagedSegment> OwnedSegments,
        int AppliedEventCount);

    private sealed record TailMergeDecision(
        bool CanMerge,
        TailMergeSkipReason? Reason,
        long? EstimatedLocalBytes,
        long? LocalBudgetBytes);

    private enum TailMergeSkipReason
    {
        Disabled = 1,
        SegmentLimits = 2,
        LocalBudgetExceeded = 3
    }

    public sealed class MergeCapacityExceededException : Exception;

}
