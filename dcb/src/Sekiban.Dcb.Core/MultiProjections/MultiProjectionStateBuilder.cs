using System.Text;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        var versionResult = _domainTypes.MultiProjectorTypes.GetProjectorVersion(projectorName);
        if (!versionResult.IsSuccess)
        {
            return new ProjectorBuildResult(
                projectorName, "unknown", BuildStatus.Failed,
                $"Failed to get projector version: {versionResult.GetException().Message}", 0);
        }
        var projectorVersion = versionResult.GetValue();

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
                var envelope = await LoadEnvelopeAsync(currentState, ct);
                await actor.SetSnapshotAsync(envelope, ct);
                startPosition = currentState.LastSortableUniqueId;
                _logger.LogDebug("  Restored from position: {StartPosition}", startPosition);
            }

            // Process events
            var eventsProcessed = await ProcessEventsToSafeWindowAsync(
                actor, startPosition, options, ct);

            // Build snapshot
            var snapshotResult = await actor.GetSnapshotAsync(canGetUnsafeState: false, ct);
            if (!snapshotResult.IsSuccess)
            {
                return new ProjectorBuildResult(
                    projectorName, projectorVersion, BuildStatus.Failed,
                    snapshotResult.GetException().Message, 0);
            }

            var snapshotEnvelope = snapshotResult.GetValue();

            // Get safe position
            var safePosition = await actor.GetSafeLastSortableUniqueIdAsync();
            var safeThreshold = actor.PeekCurrentSafeWindowThreshold();

            // v10: Serialize Envelope to JSON (no outer Gzip - payload already compressed via Custom Serializer or auto Gzip)
            var envelopeJson = JsonSerializer.Serialize(snapshotEnvelope, _domainTypes.JsonSerializerOptions);
            var envelopeBytes = Encoding.UTF8.GetBytes(envelopeJson);
            var envelopeSize = envelopeBytes.LongLength;

            // Extract original/compressed sizes from the internal state
            long originalSizeBytes = envelopeSize;
            long compressedSizeBytes = envelopeSize;
            if (snapshotEnvelope.InlineState != null)
            {
                originalSizeBytes = snapshotEnvelope.InlineState.OriginalSizeBytes;
                compressedSizeBytes = snapshotEnvelope.InlineState.CompressedSizeBytes;
            }

            // Create record with v10 format
            var totalEventsProcessed = currentEventsProcessed + eventsProcessed;

            var record = new MultiProjectionStateRecord(
                ProjectorName: projectorName,
                ProjectorVersion: projectorVersion,
                PayloadType: typeof(SerializableMultiProjectionStateEnvelope).FullName!,  // v10: Envelope type
                LastSortableUniqueId: safePosition ?? string.Empty,
                EventsProcessed: totalEventsProcessed,
                StateData: envelopeBytes,  // v10: No outer compression
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

            // Save
            var saveResult = await _stateStore.UpsertAsync(record, options.OffloadThresholdBytes, ct);
            if (!saveResult.IsSuccess)
            {
                return new ProjectorBuildResult(
                    projectorName, projectorVersion, BuildStatus.Failed,
                    saveResult.GetException().Message, 0);
            }

            _logger.LogDebug(
                "  Saved: +{EventsProcessed} events, total {TotalEventsProcessed}",
                eventsProcessed,
                totalEventsProcessed);

            return new ProjectorBuildResult(
                projectorName, projectorVersion, BuildStatus.Success, reason, eventsProcessed);
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
        var totalEventsResult = await _eventStore.GetEventCountAsync(since: null);
        if (!totalEventsResult.IsSuccess)
        {
            return (false, $"Failed to get event count: {totalEventsResult.GetException().Message}", null, 0);
        }
        var totalEvents = totalEventsResult.GetValue();

        // Get current version state
        var currentStateResult = await _stateStore.GetLatestForVersionAsync(projectorName, projectorVersion, ct);
        if (!currentStateResult.IsSuccess)
        {
            return (false, $"Failed to get current state: {currentStateResult.GetException().Message}", null, 0);
        }
        var currentStateOpt = currentStateResult.GetValue();

        if (currentStateOpt.HasValue)
        {
            var currentState = currentStateOpt.GetValue();
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
        var anyVersionResult = await _stateStore.GetLatestAnyVersionAsync(projectorName, ct);
        if (!anyVersionResult.IsSuccess)
        {
            return (false, $"Failed to get any version state: {anyVersionResult.GetException().Message}", null, 0);
        }
        var anyVersionStateOpt = anyVersionResult.GetValue();

        if (anyVersionStateOpt.HasValue)
        {
            var anyVersionState = anyVersionStateOpt.GetValue();
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
        const int batchSize = 3000;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var eventsResult = await _eventStore.ReadAllEventsAsync(since);
            if (!eventsResult.IsSuccess)
            {
                _logger.LogError(
                    eventsResult.GetException(),
                    "    Error reading events");
                break;
            }

            var allEvents = eventsResult.GetValue().ToList();
            if (allEvents.Count == 0)
            {
                break;
            }

            // Limit batch size
            var events = allEvents.Take(batchSize).ToList();

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

            await actor.AddEventsAsync(safeEvents, finishedCatchUp: false);
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
    private async Task<SerializableMultiProjectionStateEnvelope> LoadEnvelopeAsync(
        MultiProjectionStateRecord record,
        CancellationToken ct)
    {
        byte[] data;

        if (record.IsOffloaded && _blobAccessor != null && record.OffloadKey != null)
        {
            await using var offloadedStream = await _blobAccessor.OpenReadAsync(record.OffloadKey, ct);
            data = await ReadAllBytesAsync(offloadedStream, ct);
        }
        else if (record.StateData != null)
        {
            data = record.StateData;
        }
        else
        {
            throw new InvalidOperationException("StateData is null and not offloaded");
        }

        if (record.PayloadType != typeof(SerializableMultiProjectionStateEnvelope).FullName)
        {
            throw new InvalidOperationException(
                $"Legacy format not supported. PayloadType: {record.PayloadType}. Please delete old snapshots and rebuild.");
        }

        // Auto-detect format: v9 (Gzip) or v10 (plain JSON)
        string envelopeJson;
        if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
        {
            // v9 format: Gzip compressed
            envelopeJson = GzipCompression.DecompressToString(data);
        }
        else
        {
            // v10 format: Plain UTF-8 JSON
            envelopeJson = Encoding.UTF8.GetString(data);
        }

        var envelope = JsonSerializer.Deserialize<SerializableMultiProjectionStateEnvelope>(
            envelopeJson, _domainTypes.JsonSerializerOptions);

        if (envelope == null)
        {
            throw new InvalidOperationException("Failed to deserialize Envelope JSON");
        }

        return envelope;
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }
}
