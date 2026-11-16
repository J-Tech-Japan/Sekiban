using System.Text;
using System.Text.Json;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Storage.Checkpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Builder service for multi projection safe state.
///     Processes events and builds state for persistence.
/// </summary>
public class MultiProjectionStateBuilder
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;
    private readonly IMultiProjectionStateStore _stateStore;
    private readonly IBlobStorageSnapshotAccessor? _blobAccessor;
    private readonly ILogger<MultiProjectionStateBuilder> _logger;
    // SEK-G20: all checkpoint mutation goes through the sole coordinator — the builder never calls a raw store mutation.
    private readonly CheckpointMutationCoordinator _checkpointMutation;

    public MultiProjectionStateBuilder(
        DcbDomainTypes domainTypes,
        IEventStore eventStore,
        IMultiProjectionStateStore stateStore,
        IBlobStorageSnapshotAccessor? blobAccessor = null,
        ILogger<MultiProjectionStateBuilder>? logger = null)
    {
        _domainTypes = domainTypes;
        _eventStore = eventStore;
        _stateStore = stateStore;
        _blobAccessor = blobAccessor;
        _logger = logger ?? NullLogger<MultiProjectionStateBuilder>.Instance;
        _checkpointMutation = new CheckpointMutationCoordinator(stateStore, () => { });
    }

    /// <summary>
    ///     Build all registered projectors.
    /// </summary>
    public async Task<MultiProjectionBuildResult> BuildAllAsync(
        MultiProjectionBuildOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new MultiProjectionBuildOptions();
        var results = new List<ProjectorBuildResult>();

        var projectorNames = _domainTypes.MultiProjectorTypes.GetAllProjectorNames();

        foreach (var projectorName in projectorNames)
        {
            if (options.SpecificProjector != null &&
                !string.Equals(projectorName, options.SpecificProjector, StringComparison.Ordinal))
            {
                continue;
            }

            var result = await BuildProjectorAsync(projectorName, options, ct);
            results.Add(result);
        }

        return new MultiProjectionBuildResult(results);
    }

    /// <summary>
    ///     Build a single projector.
    /// </summary>
    public async Task<ProjectorBuildResult> BuildProjectorAsync(
        string projectorName,
        MultiProjectionBuildOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new MultiProjectionBuildOptions();

        // Get projector version
        string projectorVersion;
        try
        {
            projectorVersion = _domainTypes.MultiProjectorTypes.GetProjectorVersion(projectorName);
        }
        catch (Exception ex)
        {
            return new ProjectorBuildResult(
                projectorName, "unknown", BuildStatus.Failed,
                $"Failed to get projector version: {ex.Message}", 0);
        }

        _logger.LogDebug(
            "[Builder] Processing {ProjectorName} (version: {ProjectorVersion})",
            projectorName,
            projectorVersion);

        try
        {
            // Evaluate build decision
            var (shouldBuild, reason, currentState, currentEventsProcessed) =
                await EvaluateBuildDecisionAsync(projectorName, projectorVersion, options, ct);

            if (!shouldBuild)
            {
                _logger.LogDebug("  Skipped: {Reason}", reason);
                return new ProjectorBuildResult(
                    projectorName, projectorVersion, BuildStatus.Skipped, reason, currentEventsProcessed);
            }

            _logger.LogDebug("  Building: {Reason}", reason);

            if (options.DryRun)
            {
                return new ProjectorBuildResult(
                    projectorName, projectorVersion, BuildStatus.Success,
                    $"[DryRun] Would build: {reason}", 0);
            }

            // Create actor
            var actor = new GeneralMultiProjectionActor(
                _domainTypes,
                projectorName,
                new GeneralMultiProjectionActorOptions { SafeWindowMs = options.SafeWindowMs },
                _logger);

            // Restore from existing state if available
            string? startPosition = null;
            if (currentState != null)
            {
                await using var resolvedSnapshot = await LoadRestoreAsync(currentState, ct);
                await actor.SetResolvedSnapshotAsync(resolvedSnapshot, ct);
                startPosition = currentState.LastSortableUniqueId;
                _logger.LogDebug("  Restored from position: {StartPosition}", startPosition);
            }

            // Process events
            var eventsProcessed = await ProcessEventsToSafeWindowAsync(
                actor, startPosition, options, ct);

            // Build snapshot. A failure reaches the catch below, which reports it as a failed build.
            var snapshotEnvelope = await actor.BuildSnapshotEnvelopeAsync(
                canGetUnsafeState: false,
                blobAccessor: _blobAccessor,
                offloadThresholdBytes: options.OffloadThresholdBytes,
                cancellationToken: ct);

            // Get safe position
            var safePosition = await actor.GetSafeLastSortableUniqueIdAsync();
            var safeThreshold = actor.PeekCurrentSafeWindowThreshold();

            // Serialize to a temp file so large projector states do not require a second in-memory copy.
            var tempEnvelopePath = Path.Combine(
                Path.GetTempPath(),
                $"sekiban-builder-envelope-{Guid.NewGuid():N}.json");

            try
            {
                await using var envelopeStream = new FileStream(
                    tempEnvelopePath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await JsonSerializer.SerializeAsync(
                    envelopeStream,
                    snapshotEnvelope,
                    _domainTypes.JsonSerializerOptions,
                    ct);
                await envelopeStream.FlushAsync(ct);
                var envelopeSize = envelopeStream.Length;

                // Extract original/compressed sizes from the internal state
                long originalSizeBytes = envelopeSize;
                long compressedSizeBytes = envelopeSize;
                if (snapshotEnvelope.InlineState != null)
                {
                    originalSizeBytes = snapshotEnvelope.InlineState.OriginalSizeBytes;
                    compressedSizeBytes = snapshotEnvelope.InlineState.CompressedSizeBytes;
                }

                var totalEventsProcessed = currentEventsProcessed + eventsProcessed;
                var writeRequest = new MultiProjectionStateWriteRequest(
                    ProjectorName: projectorName,
                    ProjectorVersion: projectorVersion,
                    PayloadType: typeof(SerializableMultiProjectionStateEnvelope).FullName!,
                    LastSortableUniqueId: safePosition ?? string.Empty,
                    EventsProcessed: totalEventsProcessed,
                    IsOffloaded: false,
                    OffloadKey: null,
                    OffloadProvider: null,
                    OriginalSizeBytes: originalSizeBytes,
                    CompressedSizeBytes: compressedSizeBytes,
                    SafeWindowThreshold: safeThreshold.Value,
                    CreatedAt: currentState?.CreatedAt ?? DateTime.UtcNow,
                    UpdatedAt: DateTime.UtcNow,
                    BuildSource: "CLI",
                    BuildHost: Environment.MachineName);

                _logger.LogDebug(
                    "  v10: Envelope JSON size: {EnvelopeSize} bytes, payload: original={OriginalSize} compressed={CompressedSize}",
                    envelopeSize,
                    originalSizeBytes,
                    compressedSizeBytes);

                envelopeStream.Position = 0;
                var saveResult = await PersistCheckpointAsync(writeRequest, envelopeStream, options.OffloadThresholdBytes, ct);
                if (saveResult.Failure is { } saveFailure)
                {
                    return new ProjectorBuildResult(
                        projectorName, projectorVersion, BuildStatus.Failed,
                        saveFailure.Message, 0);
                }

                _logger.LogDebug(
                    "  Saved: +{EventsProcessed} events, total {TotalEventsProcessed}",
                    eventsProcessed,
                    totalEventsProcessed);

                return new ProjectorBuildResult(
                    projectorName, projectorVersion, BuildStatus.Success, reason, eventsProcessed);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempEnvelopePath))
                    {
                        File.Delete(tempEnvelopePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary builder snapshot file: {Path}", tempEnvelopePath);
                }
            }
        }
        catch (Exception ex)
        {
            return new ProjectorBuildResult(
                projectorName, projectorVersion, BuildStatus.Failed, ex.Message, 0);
        }
    }

    /// <summary>
    ///     Evaluate whether to build a projector.
    /// </summary>
    private async Task<(bool ShouldBuild, string Reason, MultiProjectionStateRecord? CurrentState, long CurrentEventsProcessed)>
        EvaluateBuildDecisionAsync(
            string projectorName,
            string projectorVersion,
            MultiProjectionBuildOptions options,
            CancellationToken ct)
    {
        if (options.Force)
        {
            return (true, "Force build requested", null, 0);
        }

        // Get total event count
        long totalEvents;
        var totalEventsRead = _eventStore.GetEventCountAsync(since: null);
        try
        {
            totalEvents = await totalEventsRead;
        }
        catch (Exception ex)
        {
            return (false, $"Failed to get event count: {ex.Message}", null, 0);
        }

        // Get current version state
        MultiProjectionStateRecord? currentState;
        var currentStateRead = _stateStore.GetLatestForVersionAsync(projectorName, projectorVersion, ct);
        try
        {
            currentState = await currentStateRead;
        }
        catch (Exception ex)
        {
            return (false, $"Failed to get current state: {ex.Message}", null, 0);
        }

        if (currentState is not null)
        {
            // Same version state exists
            var unprocessedEvents = totalEvents - currentState.EventsProcessed;
            if (unprocessedEvents >= options.MinEventThreshold)
            {
                // Enough unprocessed events -> update build
                return (true, $"Unprocessed events ({unprocessedEvents}) >= threshold ({options.MinEventThreshold})",
                    currentState, currentState.EventsProcessed);
            }
            else
            {
                // Not enough -> skip
                return (false, $"Unprocessed events ({unprocessedEvents}) < threshold ({options.MinEventThreshold})",
                    currentState, currentState.EventsProcessed);
            }
        }

        // Get any version state
        MultiProjectionStateRecord? anyVersionState;
        var anyVersionRead = _stateStore.GetLatestAnyVersionAsync(projectorName, ct);
        try
        {
            anyVersionState = await anyVersionRead;
        }
        catch (Exception ex)
        {
            return (false, $"Failed to get any version state: {ex.Message}", null, 0);
        }

        if (anyVersionState is not null)
        {
            // Old version exists
            if (anyVersionState.EventsProcessed >= options.MinEventThreshold)
            {
                // Old version has enough events -> rebuild for new version
                return (true, $"Old version has {anyVersionState.EventsProcessed} events, rebuilding for new version",
                    null, 0);
            }
        }

        // No state
        if (totalEvents >= options.MinEventThreshold)
        {
            return (true, $"No existing state, total events ({totalEvents}) >= threshold ({options.MinEventThreshold})",
                null, 0);
        }

        return (false, $"Total events ({totalEvents}) < threshold ({options.MinEventThreshold})", null, 0);
    }

    /// <summary>
    ///     Process events up to safe window.
    /// </summary>
    private async Task<long> ProcessEventsToSafeWindowAsync(
        GeneralMultiProjectionActor actor,
        string? startPosition,
        MultiProjectionBuildOptions options,
        CancellationToken ct)
    {
        long processed = 0;
        var since = startPosition != null ? new SortableUniqueId(startPosition) : (SortableUniqueId?)null;
        int batchSize = Math.Max(1, options.BatchSize);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var eventsRead = _eventStore.ReadAllSerializableEventsAsync(since, batchSize);
            IEnumerable<SerializableEvent> readEvents;
            try
            {
                readEvents = await eventsRead;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "    Error reading events");
                break;
            }

            var events = readEvents.ToList();
            if (events.Count == 0)
            {
                break;
            }

            // Filter to safe window
            var safeThreshold = actor.PeekCurrentSafeWindowThreshold();
            var safeEvents = events
                .Where(e => new SortableUniqueId(e.SortableUniqueIdValue)
                    .IsEarlierThanOrEqual(safeThreshold))
                .ToList();

            if (safeEvents.Count == 0)
            {
                // Only events beyond safe window -> done
                break;
            }

            await actor.AddSerializableEventsAsync(safeEvents, finishedCatchUp: false);
            processed += safeEvents.Count;

            _logger.LogDebug(
                "    Processed {BatchCount} events (total: {Processed})",
                safeEvents.Count,
                processed);

            since = new SortableUniqueId(safeEvents.Last().SortableUniqueIdValue);

            // If we didn't get a full batch, we're done
            if (events.Count < batchSize)
            {
                break;
            }
        }

        return processed;
    }

    /// <summary>
    ///     Load envelope from saved record.
    ///     Supports both v10 (no outer Gzip) and v9 (with outer Gzip) formats.
    /// </summary>
    private async Task<ResolvedSnapshotRestore> LoadRestoreAsync(
        MultiProjectionStateRecord record,
        CancellationToken ct)
    {
        await using var dataStream = await _stateStore.OpenStateDataReadStreamAsync(record, ct);

        if (record.PayloadType != typeof(SerializableMultiProjectionStateEnvelope).FullName)
        {
            throw new InvalidOperationException(
                $"Legacy format not supported. PayloadType: {record.PayloadType}. Please delete old snapshots and rebuild.");
        }

        var envelope = await PossiblyGzippedJsonSerializer.DeserializeAsync<SerializableMultiProjectionStateEnvelope>(
            dataStream,
            _domainTypes.JsonSerializerOptions,
            ct);

        if (envelope == null)
        {
            throw new InvalidOperationException("Failed to deserialize Envelope JSON");
        }

        return await SnapshotEnvelopeResolver.ResolveForRestoreAsync(envelope, _blobAccessor, ct);
    }

    // SEK-G20: the CLI/offline build persists through the sole checkpoint-mutation coordinator (generation/tombstone CAS
    // when capable, legacy upsert otherwise), so it cannot re-contaminate a shared row outside the tombstone protocol. The
    // builder holds NO raw store mutation reach — all mutation lives in CheckpointMutationCoordinator.
    private Task<CheckpointPersistOutcome> PersistCheckpointAsync(
        MultiProjectionStateWriteRequest writeRequest, Stream stream, int offloadThreshold, CancellationToken ct) =>
        _checkpointMutation.PersistOnceAsync(writeRequest, stream, offloadThreshold, ct);
}
