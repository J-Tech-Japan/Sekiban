using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Sekiban.Dcb.Snapshots;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     General actor implementation that manages a single multi-projector instance by name.
///     Maintains both safe and unsafe states with event buffering for handling out-of-order events.
///     Uses IDualStateAccessor for type-safe access without reflection.
/// </summary>
public class GeneralMultiProjectionActor
{

    // These are intentionally actor-local rather than grain log events: the actor owns the first observed exception
    // (and its original stack), while the grain may later persist or restore only the descriptor.
    private static readonly EventId ProjectionFaultFirstObserved = new(1401, "ProjectionFaultFirstObserved");
    private static readonly EventId ProjectionFaultReRaised = new(1402, "ProjectionFaultReRaised");

    private readonly DcbDomainTypes _domain;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly GeneralMultiProjectionActorOptions _options;
    private readonly string _projectorName;
    private readonly ICoreMultiProjectorTypes _types;
    private readonly ILogger _logger;

    // Catching up state
    private bool _isCatchedUp = true;

    // Single state accessor for all projections (wrapped if necessary)
    private IMultiProjectionPayload? _singleStateAccessor;
    private Guid _unsafeLastEventId;
    private string _unsafeLastSortableUniqueId = string.Empty;
    private int _unsafeVersion;

    // Set the instant a per-event fold throws, and never cleared by a later apply. A faulted projection has stopped
    // at a poison event; presenting its pre-crash state as a successful query is the silence issue #1075 was about.
    // While it is set, applies are rejected (so a catch-up retry cannot re-apply the events before the poison and
    // double-count them) and every read surface fails with the fault context. It clears only when the projection is
    // rebuilt from scratch — a fresh actor, or an explicit ClearFaultForRebuild — never on an unrelated success,
    // because while faulted there are no successes to be had.
    private ProjectionFaultDescriptor? _fault;

    // Dynamic SafeWindow tracking (observed stream lag)
    private double _observedLagMs; // EMA of observed lag in ms
    private DateTime _lastLagUpdateUtc = DateTime.MinValue;
    private double _maxLagMs; // Decayed running maximum of observed lag
    private DateTime _lastMaxUpdateUtc = DateTime.MinValue;

    public GeneralMultiProjectionActor(
        DcbDomainTypes domainTypes,
        string projectorName,
        GeneralMultiProjectionActorOptions? options = null,
        ILogger? logger = null)
    {
        _types = domainTypes.MultiProjectorTypes;
        _domain = domainTypes;
        _jsonOptions = domainTypes.JsonSerializerOptions;
        _projectorName = projectorName;
        _options = options ?? new GeneralMultiProjectionActorOptions();
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task AddEventsAsync(IReadOnlyList<Event> events, bool finishedCatchUp = true, EventSource source = EventSource.Unknown)
    {
        ThrowIfFaulted();

        // Initialize projectors if needed
        InitializeProjectorsIfNeeded();

        // Update catching up state
        _isCatchedUp = finishedCatchUp;

        // Update dynamic lag from stream if enabled
        if (_options.EnableDynamicSafeWindow && source == EventSource.Stream && events.Count > 0)
        {
            UpdateObservedLag(events);
        }

        var safeWindowThreshold = GetSafeWindowThreshold();

        // Sort incoming events by SortableUniqueId to ensure deterministic processing order
        // and avoid order-dependent anomalies under concurrent delivery
        var orderedDistinct = events
            .GroupBy(e => e.Id)
            .Select(g => g.First())
            .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal)
            .ToList();

        // Always use single state accessor pattern (wrapped if necessary)
        AddEventsWithSingleState(orderedDistinct, safeWindowThreshold);
    }

    public async Task AddSerializableEventsAsync(IReadOnlyList<SerializableEvent> events, bool finishedCatchUp = true)
    {
        ThrowIfFaulted();
        InitializeProjectorsIfNeeded();
        _isCatchedUp = finishedCatchUp;

        var safeWindowThreshold = GetSafeWindowThreshold();
        foreach (var se in EnumerateOrderedDistinctSerializableEvents(events))
        {
            Event ev;
            try
            {
                var payload
                    = _domain.EventTypes.DeserializeEventPayload(
                        se.EventPayloadName,
                        Encoding.UTF8.GetString(se.Payload)) ??
                    throw new InvalidOperationException($"Unknown event type: {se.EventPayloadName}");
                ev = new Event(
                    payload,
                    se.SortableUniqueIdValue,
                    se.EventPayloadName,
                    se.Id,
                    se.EventMetadata,
                    se.Tags);
            }
            catch (Exception ex)
            {
                // The deserialize boundary is authoritative too: a payload that cannot be turned into an event
                // (the issue #1074 casing failure among them) faults the projection at the event it choked on, with
                // the same "point at the poison, rethrow the original" rule the fold uses.
                CaptureSerializableFaultAndRethrow(se, ex);
                throw; // unreachable: CaptureSerializableFaultAndRethrow always throws.
            }

            ApplyEventToSingleState(ev, safeWindowThreshold);
        }

        await Task.CompletedTask;
    }

    /// <summary>The fold fault this projection is stuck on, or null when it is healthy or merely catching up.</summary>
    public ProjectionFaultDescriptor? CurrentFault => _fault;

    /// <summary>True when a per-event fold has failed and the projection has not been rebuilt since.</summary>
    public bool IsFaulted => _fault is not null;

    /// <summary>
    ///     Re-establishes a fault that was persisted across a restart, so a freshly-activated grain cannot answer a
    ///     query successfully before its known fault has been re-established.
    /// </summary>
    public void RestoreFault(ProjectionFaultDescriptor fault) => _fault = fault;

    /// <summary>
    ///     Clears the fault for a deliberate rebuild. The caller is responsible for replaying from a clean state; if
    ///     the same poison event is reached again the fault simply re-establishes. This is the ONLY way a fault clears
    ///     short of constructing a new actor — an ordinary successful apply never clears it, because a faulted
    ///     projection rejects applies.
    /// </summary>
    public void ClearFaultForRebuild() => _fault = null;

    public Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true)
    {
        // A faulted projection has no trustworthy state to return. Every read surface — state, and through it the
        // scalar and list query surfaces that all fetch state here first — fails with the fault context instead of
        // handing back the last pre-crash payload as a success.
        if (_fault is not null)
        {
            return Task.FromResult(ResultBox.Error<MultiProjectionState>(CreateFaultException(_fault)));
        }

        InitializeProjectorsIfNeeded();

        return GetStateFromSingleAccessorAsync(canGetUnsafeState);
    }

    private SekibanProjectionFaultException CreateFaultException(ProjectionFaultDescriptor fault)
    {
        var exception = new SekibanProjectionFaultException(fault);
        _logger.LogWarning(
            ProjectionFaultReRaised,
            "Projection fault re-raised from its persisted descriptor: {ProjectorName}, EventId: {EventId}, Position: {Position}, IsReRaise: {IsReRaise}",
            fault.ProjectorName,
            fault.EventId,
            fault.Position,
            exception.IsReRaise);
        return exception;
    }

    /// <summary>Rejects an apply on a faulted projection, so a catch-up retry cannot re-run the pre-poison events.</summary>
    private void ThrowIfFaulted()
    {
        if (_fault is not null)
        {
            throw CreateFaultException(_fault);
        }
    }

    public async Task<ProjectionHeadStatus> GetProjectionHeadStatusAsync()
    {
        InitializeProjectorsIfNeeded();

        var versionResult = _types.GetProjectorVersion(_projectorName);
        if (!versionResult.IsSuccess)
        {
            throw versionResult.GetException();
        }

        var safeWindowThreshold = GetSafeWindowThreshold();

        if (_singleStateAccessor is IDualStateAccessor dualAccessor)
        {
            try
            {
                dualAccessor.PromoteBufferedEvents(safeWindowThreshold, _domain);
            }
            catch (Exception)
            {
                // Head-status reads are observational; ignore best-effort safe-window promotion failures here.
            }

            var current = new ProjectionPosition(
                dualAccessor.UnsafeVersion,
                ProjectionHeadStatusUtilities.NormalizeSortableUniqueId(dualAccessor.UnsafeLastSortableUniqueId));
            var consistent = new ProjectionPosition(
                dualAccessor.SafeVersion,
                ProjectionHeadStatusUtilities.NormalizeSortableUniqueId(dualAccessor.SafeLastSortableUniqueId));

            return CreateProjectionHeadStatus(versionResult.GetValue(), current, consistent);
        }

        var currentStateResult = await GetStateAsync(canGetUnsafeState: true);
        if (!currentStateResult.IsSuccess)
        {
            throw currentStateResult.GetException();
        }

        var consistentStateResult = await GetStateAsync(canGetUnsafeState: false);
        if (!consistentStateResult.IsSuccess)
        {
            throw consistentStateResult.GetException();
        }

        return CreateProjectionHeadStatus(
            versionResult.GetValue(),
            ToProjectionPosition(currentStateResult.GetValue()),
            ToProjectionPosition(consistentStateResult.GetValue()));
    }

    public Task SetCurrentState(SerializableMultiProjectionState state)
    {
        // Validate projector version before restoring state
        var versionResult = _types.GetProjectorVersion(_projectorName);
        if (!versionResult.IsSuccess)
        {
            throw versionResult.GetException();
        }

        var currentVersion = versionResult.GetValue();
        if (!string.Equals(currentVersion, state.ProjectorVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Snapshot projector version mismatch. Current='{currentVersion}', Snapshot='{state.ProjectorVersion}' for projector '{_projectorName}'.");
        }

        RestorePayload(state);
        return Task.CompletedTask;
    }

    // Compatibility restore: ignore projector version mismatch and restore snapshot payload as-is
    public Task SetCurrentStateIgnoringVersion(SerializableMultiProjectionState state)
    {
        RestorePayload(state);
        return Task.CompletedTask;
    }

    private void RestorePayload(SerializableMultiProjectionState state)
    {
        var payloadBytes = state.GetPayloadBytes();
        var projTypeRb = _types.GetProjectorType(state.ProjectorName);
        if (!projTypeRb.IsSuccess) throw projTypeRb.GetException();

        var safeThreshold = GetSafeWindowThreshold();
        var deserializeResult = _types.Deserialize(state.ProjectorName, _domain, safeThreshold.Value, payloadBytes);
        if (!deserializeResult.IsSuccess) throw deserializeResult.GetException();

        var loadedPayload = deserializeResult.GetValue();
        _logger.LogDebug("[{ProjectorName}] Deserialize: via ICoreMultiProjectorTypes", _projectorName);

        if (loadedPayload is IDualStateAccessor)
        {
            _singleStateAccessor = loadedPayload;
        }
        else
        {
            _singleStateAccessor = DualStateProjectionWrapperFactory.CreateFromRestoredSnapshot(
                loadedPayload,
                _projectorName,
                _types,
                _domain,
                safeThreshold.Value,
                initialVersion: state.Version,
                initialLastEventId: state.LastEventId,
                initialLastSortableUniqueId: state.LastSortableUniqueId);

            if (_singleStateAccessor == null)
            {
                throw new InvalidOperationException($"Failed to create wrapper for projector {_projectorName}");
            }
        }

        // Update tracking variables
        _unsafeLastEventId = state.LastEventId;
        _unsafeLastSortableUniqueId = state.LastSortableUniqueId;
        _unsafeVersion = state.Version;
        _isCatchedUp = state.IsCatchedUp;
    }

    public async Task<ResultBox<SerializedMultiProjectionStatePayload>> BuildSerializedStatePayloadAsync(bool canGetUnsafeState)
    {
        InitializeProjectorsIfNeeded();
        var stateResult = await GetStateAsync(canGetUnsafeState);
        if (!stateResult.IsSuccess) return ResultBox.Error<SerializedMultiProjectionStatePayload>(stateResult.GetException());

        var multiProjectionState = stateResult.GetValue();
        var payload = multiProjectionState.Payload;
        var versionResult = _types.GetProjectorVersion(_projectorName);
        if (!versionResult.IsSuccess) return ResultBox.Error<SerializedMultiProjectionStatePayload>(versionResult.GetException());
        var projectorVersion = versionResult.GetValue();

        try
        {
            var safeThreshold = GetSafeWindowThreshold();
            var serializeResult = _types.Serialize(_projectorName, _domain, safeThreshold.Value, payload);
            if (!serializeResult.IsSuccess)
            {
                return ResultBox.Error<SerializedMultiProjectionStatePayload>(serializeResult.GetException());
            }

            var result = serializeResult.GetValue();
            _logger.LogDebug(
                "[{ProjectorName}] Serialize(binary): via ICoreMultiProjectorTypes len={CompressedBytes} (original={OriginalBytes}, ratio={CompressionRatio:P1})",
                _projectorName,
                result.CompressedSizeBytes,
                result.OriginalSizeBytes,
                result.CompressionRatio);
            var payloadType = payload.GetType();
            return ResultBox.FromValue(new SerializedMultiProjectionStatePayload(
                result.Data,
                payloadType.FullName ?? payloadType.Name,
                _projectorName,
                projectorVersion,
                multiProjectionState.LastSortableUniqueId,
                multiProjectionState.LastEventId,
                multiProjectionState.Version,
                _isCatchedUp,
                multiProjectionState.IsSafeState,
                result.OriginalSizeBytes,
                result.CompressedSizeBytes));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializedMultiProjectionStatePayload>(ex);
        }
    }

    /// <summary>
    ///     Returns a snapshot envelope, optionally offloading the payload when an accessor and threshold are provided.
    /// </summary>
    public async Task<ResultBox<SerializableMultiProjectionStateEnvelope>> BuildSnapshotEnvelopeAsync(
        bool canGetUnsafeState = true,
        IBlobStorageSnapshotAccessor? blobAccessor = null,
        int offloadThresholdBytes = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        var payloadResult = await BuildSerializedStatePayloadAsync(canGetUnsafeState);
        if (!payloadResult.IsSuccess)
        {
            return ResultBox.Error<SerializableMultiProjectionStateEnvelope>(payloadResult.GetException());
        }

        var payload = payloadResult.GetValue();
        if (blobAccessor is not null &&
            offloadThresholdBytes > 0 &&
            payload.PayloadBytes.LongLength > offloadThresholdBytes)
        {
            await using var uploadStream = new MemoryStream(payload.PayloadBytes, writable: false);
            var offloadKey = await blobAccessor.WriteAsync(uploadStream, payload.ProjectorName, cancellationToken)
                .ConfigureAwait(false);
            return ResultBox.FromValue(new SerializableMultiProjectionStateEnvelope(
                IsOffloaded: true,
                InlineState: null,
                OffloadedState: payload.ToOffloadedState(offloadKey, blobAccessor.ProviderName)));
        }

        return ResultBox.FromValue(new SerializableMultiProjectionStateEnvelope(
            IsOffloaded: false,
            InlineState: payload.ToInlineState(),
            OffloadedState: null));
    }

    /// <summary>
    ///     Stream-first variant of <see cref="BuildSnapshotEnvelopeAsync" />.
    ///     Serializes the snapshot payload directly into a spillable buffer (supplied by
    ///     <paramref name="payloadBufferProvider" />) so that a very large multi-projection
    ///     snapshot never has to materialize a full compressed <see cref="byte" />[] in managed
    ///     memory before the offload decision is made.
    ///     When the buffered payload exceeds <paramref name="offloadThresholdBytes" /> and a
    ///     <paramref name="blobAccessor" /> is provided, the buffer stream is uploaded directly
    ///     to blob storage and an offloaded envelope (metadata only) is returned. Otherwise,
    ///     the buffered bytes are materialized for the inline envelope — which is safe because
    ///     inline payloads are, by definition, below the offload threshold.
    ///     If no buffer provider is supplied the method falls back to <see cref="BuildSnapshotEnvelopeAsync" />.
    /// </summary>
    public async Task<ResultBox<SerializableMultiProjectionStateEnvelope>> BuildSnapshotEnvelopeStreamFirstAsync(
        ISnapshotPayloadBufferProvider? payloadBufferProvider,
        bool canGetUnsafeState = true,
        IBlobStorageSnapshotAccessor? blobAccessor = null,
        int offloadThresholdBytes = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        // Without a spill buffer we cannot stream-first; fall back to the legacy path.
        if (payloadBufferProvider is null)
        {
            return await BuildSnapshotEnvelopeAsync(
                canGetUnsafeState,
                blobAccessor,
                offloadThresholdBytes,
                cancellationToken).ConfigureAwait(false);
        }

        InitializeProjectorsIfNeeded();

        var stateResult = await GetStateAsync(canGetUnsafeState);
        if (!stateResult.IsSuccess)
        {
            return ResultBox.Error<SerializableMultiProjectionStateEnvelope>(stateResult.GetException());
        }

        var multiProjectionState = stateResult.GetValue();
        var payload = multiProjectionState.Payload;
        var payloadType = payload.GetType();
        var payloadTypeName = payloadType.FullName ?? payloadType.Name;

        var versionResult = _types.GetProjectorVersion(_projectorName);
        if (!versionResult.IsSuccess)
        {
            return ResultBox.Error<SerializableMultiProjectionStateEnvelope>(versionResult.GetException());
        }
        var projectorVersion = versionResult.GetValue();

        var safeThreshold = GetSafeWindowThreshold();

        await using var buffer = await payloadBufferProvider
            .CreateBufferAsync(_projectorName, cancellationToken)
            .ConfigureAwait(false);

        var bufferStream = buffer.Stream;
        if (!bufferStream.CanRead || !bufferStream.CanWrite || !bufferStream.CanSeek)
        {
            return ResultBox.Error<SerializableMultiProjectionStateEnvelope>(
                new InvalidOperationException(
                    "ISnapshotPayloadBufferProvider must return a readable, writable, seekable stream."));
        }

        // Reset in case the provider reused a stream.
        if (bufferStream.Length > 0)
        {
            bufferStream.SetLength(0);
        }
        bufferStream.Position = 0;

        SerializationSizeInfo sizeInfo;
        try
        {
            var serializeResult = _types.SerializeToStream(
                _projectorName,
                _domain,
                safeThreshold.Value,
                payload,
                bufferStream);
            if (!serializeResult.IsSuccess)
            {
                return ResultBox.Error<SerializableMultiProjectionStateEnvelope>(serializeResult.GetException());
            }

            sizeInfo = serializeResult.GetValue();
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializableMultiProjectionStateEnvelope>(ex);
        }

        await bufferStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        var compressedLength = bufferStream.Length;
        // Reconcile compressed size reported by the serializer with the actual stream length
        // (SerializeToStream may return originalSize when the destination is not seekable).
        var originalSize = sizeInfo.OriginalSizeBytes;
        var compressedSize = Math.Max(sizeInfo.CompressedSizeBytes, compressedLength);

        _logger.LogDebug(
            "[{ProjectorName}] StreamFirstSerialize: location={Location} original={OriginalBytes} compressed={CompressedBytes}",
            _projectorName,
            buffer.Location,
            originalSize,
            compressedSize);

        var shouldOffload = blobAccessor is not null
            && offloadThresholdBytes > 0
            && compressedLength > offloadThresholdBytes;

        if (shouldOffload)
        {
            bufferStream.Position = 0;
            var offloadKey = await blobAccessor!
                .WriteAsync(bufferStream, _projectorName, cancellationToken)
                .ConfigureAwait(false);

            var offloadedState = new SerializableMultiProjectionStateOffloaded(
                OffloadKey: offloadKey,
                StorageProvider: blobAccessor.ProviderName,
                MultiProjectionPayloadType: payloadTypeName,
                ProjectorName: _projectorName,
                ProjectorVersion: projectorVersion,
                LastSortableUniqueId: multiProjectionState.LastSortableUniqueId,
                LastEventId: multiProjectionState.LastEventId,
                Version: multiProjectionState.Version,
                IsCatchedUp: _isCatchedUp,
                IsSafeState: multiProjectionState.IsSafeState,
                PayloadLength: compressedLength,
                OriginalSizeBytes: originalSize,
                CompressedSizeBytes: compressedSize);

            return ResultBox.FromValue(new SerializableMultiProjectionStateEnvelope(
                IsOffloaded: true,
                InlineState: null,
                OffloadedState: offloadedState));
        }

        // Inline path: rewind and rematerialize the (sub-threshold) payload bytes for the
        // Base64 field in the envelope. Since inline payloads are below the offload threshold
        // by construction, this does not cause the large-memory spike the stream-first path
        // is designed to avoid.
        if (compressedLength > int.MaxValue)
        {
            return ResultBox.Error<SerializableMultiProjectionStateEnvelope>(
                new InvalidOperationException(
                    $"Inline snapshot payload size {compressedLength} exceeds int.MaxValue; the payload must be offloaded via IBlobStorageSnapshotAccessor."));
        }

        bufferStream.Position = 0;
        var payloadBytes = new byte[(int)compressedLength];
        try
        {
            await bufferStream.ReadExactlyAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException ex)
        {
            return ResultBox.Error<SerializableMultiProjectionStateEnvelope>(
                new InvalidOperationException(
                    $"Snapshot payload buffer returned fewer bytes than the declared length {compressedLength} for projector {_projectorName}.",
                    ex));
        }

        var inlineState = SerializableMultiProjectionState.FromBytes(
            payloadBytes,
            payloadTypeName,
            _projectorName,
            projectorVersion,
            multiProjectionState.LastSortableUniqueId,
            multiProjectionState.LastEventId,
            multiProjectionState.Version,
            _isCatchedUp,
            multiProjectionState.IsSafeState,
            originalSize,
            compressedSize);

        return ResultBox.FromValue(new SerializableMultiProjectionStateEnvelope(
            IsOffloaded: false,
            InlineState: inlineState,
            OffloadedState: null));
    }

    /// <summary>
    ///     Returns a snapshot envelope containing an inline payload.
    /// </summary>
    public Task<ResultBox<SerializableMultiProjectionStateEnvelope>> GetSnapshotAsync(
        bool canGetUnsafeState = true,
        CancellationToken cancellationToken = default) =>
        BuildSnapshotEnvelopeAsync(canGetUnsafeState, null, int.MaxValue, cancellationToken);

    private async Task<ResultBox<SerializableMultiProjectionState>> BuildSerializableStateAsync(bool canGetUnsafeState)
    {
        var payloadResult = await BuildSerializedStatePayloadAsync(canGetUnsafeState);
        if (!payloadResult.IsSuccess)
        {
            return ResultBox.Error<SerializableMultiProjectionState>(payloadResult.GetException());
        }

        return ResultBox.FromValue(payloadResult.GetValue().ToInlineState());
    }

    /// <summary>
    ///     Restores this actor from a snapshot envelope containing an inline payload.
    /// </summary>
    public async Task SetSnapshotAsync(SerializableMultiProjectionStateEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.IsOffloaded)
        {
            throw new InvalidOperationException(
                "Offloaded snapshots are not supported by the actor. Restore from the state store before applying.");
        }

        if (envelope.InlineState == null)
            throw new InvalidOperationException("Inline snapshot missing InlineState");
        await SetCurrentState(envelope.InlineState);
    }

    /// <summary>
    ///     Builds a snapshot envelope (with offload if configured), serializes it to JSON, evaluates size limit,
    ///     and returns the data needed for persistence (JSON and safe position).
    ///     Size limit is controlled by GeneralMultiProjectionActorOptions.MaxSnapshotSerializedSizeBytes (<=0 to disable).
    /// </summary>
    public async Task<ResultBox<SnapshotPersistenceData>> BuildSnapshotForPersistenceAsync(
        bool canGetUnsafeState = false,
        IBlobStorageSnapshotAccessor? blobAccessor = null,
        int offloadThresholdBytes = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        // Promote buffered events before building a safe snapshot
        try
        {
            ForcePromoteBufferedEvents();
        }
        catch
        {
            // Ignore promotion errors to avoid blocking persistence
        }
        var envelopeRb = await BuildSnapshotEnvelopeAsync(
            canGetUnsafeState,
            blobAccessor,
            offloadThresholdBytes,
            cancellationToken);
        if (!envelopeRb.IsSuccess)
        {
            return ResultBox.Error<SnapshotPersistenceData>(envelopeRb.GetException());
        }

        var envelope = envelopeRb.GetValue();
        await using var jsonStream = new MemoryStream();
        await JsonSerializer.SerializeAsync(jsonStream, envelope, _jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        var size = jsonStream.Length;

        if (size > int.MaxValue)
        {
            return ResultBox.Error<SnapshotPersistenceData>(
                new InvalidOperationException(
                    $"Snapshot size {size} exceeds supported string conversion limit {int.MaxValue}."));
        }

        if (_options.MaxSnapshotSerializedSizeBytes > 0 && size > _options.MaxSnapshotSerializedSizeBytes)
        {
            return ResultBox.Error<SnapshotPersistenceData>(
                new InvalidOperationException(
                    $"Snapshot size {size} exceeds limit {_options.MaxSnapshotSerializedSizeBytes}"));
        }

        // Safe position for hosts to persist
        var safePosition = await GetSafeLastSortableUniqueIdAsync();
        var json = Encoding.UTF8.GetString(jsonStream.GetBuffer(), 0, (int)size);
        return ResultBox.FromValue(new SnapshotPersistenceData(json, (int)size, safePosition));
    }

    public async Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId)
    {
        if (string.IsNullOrEmpty(sortableUniqueId))
        {
            return false;
        }

        // Use UNSAFE progress for responsiveness: we only need to know whether
        // the actor has ingested the event, not whether it is outside the safe window.
        // This keeps waitForSortableUniqueId from blocking ~SafeWindowMs unnecessarily.
        var stateRb = await GetStateAsync(canGetUnsafeState: true);
        if (!stateRb.IsSuccess)
        {
            return false;
        }
        var last = stateRb.GetValue().LastSortableUniqueId ?? string.Empty;
        if (string.IsNullOrEmpty(last)) return false;
        return string.Compare(sortableUniqueId, last, StringComparison.Ordinal) <= 0;
    }

    /// <summary>
    ///     Gets the last safe (persisted) sortable unique id. Used by hosts to persist SafeLastPosition.
    /// </summary>
    public async Task<string> GetSafeLastSortableUniqueIdAsync()
    {
        InitializeProjectorsIfNeeded();

        // Get safe state to retrieve its last sortable unique ID
        var stateResult = await GetStateAsync(canGetUnsafeState: false);
        if (stateResult.IsSuccess)
        {
            return stateResult.GetValue().LastSortableUniqueId;
        }

        return string.Empty;
    }

    private void AddEventsWithSingleState(IReadOnlyList<Event> events, SortableUniqueId safeWindowThreshold)
    {
        foreach (var ev in events)
        {
            ApplyEventToSingleState(ev, safeWindowThreshold);
        }
    }

    private void ApplyEventToSingleState(Event ev, SortableUniqueId safeWindowThreshold)
    {
        if (_singleStateAccessor is IDualStateAccessor dualAccessor)
        {
            // Use IDualStateAccessor for type-safe event processing without reflection
            try
            {
                dualAccessor = dualAccessor.ProcessEventAs(ev, safeWindowThreshold, _domain);

                // Update tracking
                _unsafeLastEventId = ev.Id;
                if (string.IsNullOrEmpty(_unsafeLastSortableUniqueId) ||
                    string.Compare(ev.SortableUniqueIdValue, _unsafeLastSortableUniqueId, StringComparison.Ordinal) > 0)
                {
                    _unsafeLastSortableUniqueId = ev.SortableUniqueIdValue;
                }
                _unsafeVersion++;
            }
            catch (Exception ex)
            {
                CaptureFaultAndRethrow(ev, ex);
            }

            // The accessor may return 'this' (same instance) or a new instance
            if (dualAccessor is IMultiProjectionPayload updatedPayload)
            {
                _singleStateAccessor = updatedPayload;
            }
        }
        else
        {
            // Fallback for projectors that implement ISafeAndUnsafeStateAccessor<T> directly
            // (not wrapped in DualStateProjectionWrapper)
            var accessorType = _singleStateAccessor!.GetType();
            var method = accessorType.GetMethod("ProcessEvent");
            if (method == null)
            {
                throw new InvalidOperationException($"ProcessEvent method not found on {accessorType.Name} for projector {_projectorName}");
            }

            try
            {
                var result = method.Invoke(_singleStateAccessor, new object[] { ev, safeWindowThreshold, _domain });

                if (result is IMultiProjectionPayload payload)
                {
                    _singleStateAccessor = payload;
                }
                else if (result == null)
                {
                    throw new InvalidOperationException($"ProcessEvent returned null for projector {_projectorName}");
                }
                else
                {
                    throw new InvalidOperationException($"ProcessEvent returned incompatible type for projector {_projectorName}: {result.GetType().FullName}");
                }

                _unsafeLastEventId = ev.Id;
                if (string.IsNullOrEmpty(_unsafeLastSortableUniqueId) ||
                    string.Compare(ev.SortableUniqueIdValue, _unsafeLastSortableUniqueId, StringComparison.Ordinal) > 0)
                {
                    _unsafeLastSortableUniqueId = ev.SortableUniqueIdValue;
                }
                _unsafeVersion++;
            }
            catch (Exception ex)
            {
                CaptureFaultAndRethrow(ev, ex);
            }
        }
    }

    /// <summary>
    ///     The one place a projection fault is born: the per-event fold boundary, where the poison event can be named.
    ///     The event's identity and position are captured from <paramref name="ev" /> — BEFORE any tracking field
    ///     advanced, because the fold threw — so the descriptor points at the event that could not be folded, not at
    ///     wherever execution went next. The ORIGINAL exception instance is rethrown, never re-wrapped, carrying the
    ///     fault's identifiers on its Data (SEK-G9's boundaries then read them like any other annotated failure).
    /// </summary>
    private void CaptureFaultAndRethrow(Event ev, Exception ex)
    {
        var original = ex.InnerException ?? ex;

        if (_fault is null)
        {
            _fault = new ProjectionFaultDescriptor(
                ev.Id,
                ev.EventType,
                _projectorName,
                ev.SortableUniqueIdValue,
                original.Message,
                DateTime.UtcNow.Ticks);
            // The original exception is available only at this boundary. Emit it exactly once, with its real stack;
            // later calls have only the descriptor and use the distinct re-raise event id.
            _logger.LogError(
                ProjectionFaultFirstObserved,
                original,
                "Projection fault first observed: {ProjectorName}, EventId: {EventId}, Position: {Position}",
                _fault.ProjectorName,
                _fault.EventId,
                _fault.Position);
        }

        _fault.Annotate(original);
        ExceptionDispatchInfo.Capture(original).Throw();
    }

    /// <summary>The deserialize-boundary counterpart of <see cref="CaptureFaultAndRethrow" />, before an Event exists.</summary>
    private void CaptureSerializableFaultAndRethrow(SerializableEvent se, Exception ex)
    {
        var original = ex.InnerException ?? ex;

        if (_fault is null)
        {
            _fault = new ProjectionFaultDescriptor(
                se.Id,
                se.EventPayloadName,
                _projectorName,
                se.SortableUniqueIdValue,
                original.Message,
                DateTime.UtcNow.Ticks);
            _logger.LogError(
                ProjectionFaultFirstObserved,
                original,
                "Projection fault first observed: {ProjectorName}, EventId: {EventId}, Position: {Position}",
                _fault.ProjectorName,
                _fault.EventId,
                _fault.Position);
        }

        _fault.Annotate(original);
        ExceptionDispatchInfo.Capture(original).Throw();
    }

    private static IEnumerable<SerializableEvent> EnumerateOrderedDistinctSerializableEvents(
        IReadOnlyList<SerializableEvent> events)
    {
        if (events.Count <= 1)
        {
            return events;
        }

        var seenIds = new HashSet<Guid>();
        var previousSortableUniqueId = events[0].SortableUniqueIdValue;
        var alreadyOrderedDistinct = true;

        foreach (var ev in events)
        {
            if (!seenIds.Add(ev.Id) ||
                string.Compare(ev.SortableUniqueIdValue, previousSortableUniqueId, StringComparison.Ordinal) < 0)
            {
                alreadyOrderedDistinct = false;
                break;
            }

            previousSortableUniqueId = ev.SortableUniqueIdValue;
        }

        return alreadyOrderedDistinct
            ? events
            : events
                .GroupBy(e => e.Id)
                .Select(g => g.First())
                .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal);
    }

    public void ForcePromoteBufferedEvents()
    {
        if (_singleStateAccessor is IDualStateAccessor dualAccessor)
        {
            var threshold = GetSafeWindowThreshold();
            dualAccessor.PromoteBufferedEvents(threshold, _domain);
            return;
        }

        // Fallback for non-IDualStateAccessor implementations
        if (_singleStateAccessor == null) return;
        try
        {
            var accessorType = _singleStateAccessor.GetType();
            var getSafeMethod = accessorType.GetMethod("GetSafeProjection");
            if (getSafeMethod != null)
            {
                var threshold = GetSafeWindowThreshold();
                _ = getSafeMethod.Invoke(_singleStateAccessor, new object[] { threshold, _domain });
            }
        }
        catch
        {
            // Swallow - best effort only
        }
    }

    public void CompactSafeHistory()
    {
        if (_singleStateAccessor is IDualStateAccessor dualAccessor)
        {
            dualAccessor.CompactSafeHistory();
        }
    }

    /// <summary>
    ///     SEK-G18 INTERNAL seam (no public API change): true when the dual-state accessor detected an out-of-global-order
    ///     safe promotion / arrival that the incremental (compacted-baseline) path cannot reorder. The grain/host responds
    ///     with a full ordered rebuild from the authoritative event store — NOT the G14 fault path. Visible to the Orleans
    ///     host via InternalsVisibleTo; not part of the public surface.
    /// </summary>
    internal bool RebuildRequired =>
        _singleStateAccessor is IDualStateRebuildSignals signals && signals.RebuildRequired;

    /// <summary>SEK-G20: the offending event id behind <see cref="RebuildRequired" /> (null if none). Internal seam.</summary>
    internal string? RebuildOffendingEventId =>
        _singleStateAccessor is IDualStateRebuildSignals signals ? signals.RebuildOffendingEventId : null;

    /// <summary>SEK-G20: the offending event position behind <see cref="RebuildRequired" /> (null if none). Internal seam.</summary>
    internal string? RebuildOffendingPosition =>
        _singleStateAccessor is IDualStateRebuildSignals signals ? signals.RebuildOffendingPosition : null;

    public void ForcePromoteAllBufferedEvents()
    {
        if (_singleStateAccessor is IDualStateAccessor dualAccessor)
        {
            var maxThreshold = SortableUniqueId.MaxValue;
            dualAccessor.PromoteBufferedEvents(maxThreshold, _domain);
            _logger.LogDebug(
                "[SafePromotion] projector={ProjectorName} force-promote-all invoked threshold={Threshold}",
                _projectorName,
                maxThreshold.Value);
            return;
        }

        // Fallback for non-IDualStateAccessor implementations
        if (_singleStateAccessor == null) return;
        try
        {
            var accessorType = _singleStateAccessor.GetType();
            var getSafeMethod = accessorType.GetMethod("GetSafeProjection");
            if (getSafeMethod != null)
            {
                var maxThreshold = SortableUniqueId.MaxValue;
                _ = getSafeMethod.Invoke(_singleStateAccessor, new object[] { maxThreshold, _domain });
                _logger.LogDebug(
                    "[SafePromotion] projector={ProjectorName} force-promote-all invoked threshold={Threshold}",
                    _projectorName,
                    maxThreshold.Value);
            }
        }
        catch { }
    }


    private Task<ResultBox<MultiProjectionState>> GetStateFromSingleAccessorAsync(bool canGetUnsafeState)
    {
        // Get version from the type
        var versionResult = _types.GetProjectorVersion(_projectorName);
        if (!versionResult.IsSuccess)
        {
            return Task.FromResult(ResultBox.Error<MultiProjectionState>(versionResult.GetException()));
        }
        var version = versionResult.GetValue();

        var safeWindowThreshold = GetSafeWindowThreshold();

        IMultiProjectionPayload statePayload;
        bool isSafeState;
        string lastSortableId;
        Guid lastEventId;
        int stateVersion;

        if (_singleStateAccessor is IDualStateAccessor dualAccessor)
        {
            // Use IDualStateAccessor for type-safe access without reflection
            if (canGetUnsafeState)
            {
                // Promote buffered events before reading unsafe state
                try { dualAccessor.PromoteBufferedEvents(safeWindowThreshold, _domain); } catch { }

                statePayload = (IMultiProjectionPayload)dualAccessor.GetUnsafeProjectorPayload();
                lastSortableId = dualAccessor.UnsafeLastSortableUniqueId;
                lastEventId = dualAccessor.UnsafeLastEventId;
                stateVersion = dualAccessor.UnsafeVersion;

                // SEK-G18: IsSafeState reflects the RECONCILE FACT — the served state was published identical to the safe
                // state (no buffered events remain, no rebuild pending) — never a timestamp comparison that can report
                // true for an unreconciled, arrival-ordered payload (#1092). Read via the internal signal seam; fall back
                // to the legacy timestamp comparison only for external accessors that do not implement the seam.
                if (dualAccessor is IDualStateRebuildSignals signals)
                {
                    isSafeState = signals.IsServedIdenticalToSafe;
                }
                else if (!string.IsNullOrEmpty(lastSortableId))
                {
                    isSafeState = new SortableUniqueId(lastSortableId).GetDateTime() <= safeWindowThreshold.GetDateTime();
                }
                else
                {
                    isSafeState = true;
                }
            }
            else
            {
                // Promote buffered events and get safe state
                dualAccessor.PromoteBufferedEvents(safeWindowThreshold, _domain);

                statePayload = (IMultiProjectionPayload)dualAccessor.GetSafeProjectorPayload();
                var safeSortableId = dualAccessor.SafeLastSortableUniqueId;
                lastSortableId = safeSortableId ?? string.Empty;
                lastEventId = Guid.Empty;
                stateVersion = dualAccessor.SafeVersion;
                isSafeState = true;
            }
        }
        else
        {
            // Fallback: use reflection for projectors that implement ISafeAndUnsafeStateAccessor<T> directly
            var accessorType = _singleStateAccessor!.GetType();

            if (canGetUnsafeState)
            {
                try
                {
                    var autoSafeMethod = accessorType.GetMethod("GetSafeProjection");
                    if (autoSafeMethod != null)
                    {
                        _ = autoSafeMethod.Invoke(_singleStateAccessor, new object[] { safeWindowThreshold, _domain });
                    }
                }
                catch { }
                var getUnsafeMethod = accessorType.GetMethod("GetUnsafeProjection");
                var projection = getUnsafeMethod?.Invoke(_singleStateAccessor, new object[] { _domain });
                statePayload = (IMultiProjectionPayload)(projection?.GetType().GetProperty("State")?.GetValue(projection) ?? _singleStateAccessor);
                lastSortableId = (string)(projection?.GetType().GetProperty("LastSortableUniqueId")?.GetValue(projection) ?? _unsafeLastSortableUniqueId);
                lastEventId = (Guid)(projection?.GetType().GetProperty("LastEventId")?.GetValue(projection) ?? _unsafeLastEventId);
                stateVersion = (int)(projection?.GetType().GetProperty("Version")?.GetValue(projection) ?? _unsafeVersion);

                if (!string.IsNullOrEmpty(lastSortableId))
                {
                    var lastEventTime = new SortableUniqueId(lastSortableId).GetDateTime();
                    var safeThresholdTime = safeWindowThreshold.GetDateTime();
                    isSafeState = lastEventTime <= safeThresholdTime;
                }
                else
                {
                    isSafeState = true;
                }
            }
            else
            {
                var getSafeMethod = accessorType.GetMethod("GetSafeProjection");
                var projection = getSafeMethod?.Invoke(_singleStateAccessor, new object[] { safeWindowThreshold, _domain });
                statePayload = (IMultiProjectionPayload)(projection?.GetType().GetProperty("State")?.GetValue(projection) ?? _singleStateAccessor);
                var safeSortableId = projection?.GetType().GetProperty("SafeLastSortableUniqueId")?.GetValue(projection) as string;
                lastSortableId = safeSortableId ?? string.Empty;
                lastEventId = Guid.Empty;
                stateVersion = (int)(projection?.GetType().GetProperty("Version")?.GetValue(projection) ?? _unsafeVersion);
                isSafeState = true;
            }
        }

        var state = new MultiProjectionState(
            statePayload,
            _projectorName,
            version,
            lastSortableId,
            lastEventId,
            stateVersion,
            _isCatchedUp,
            isSafeState
        );

        return Task.FromResult(ResultBox.FromValue(state));
    }


    /// <summary>
    ///     Get the unsafe state which includes all events including those within SafeWindow
    /// </summary>
    public Task<ResultBox<MultiProjectionState>> GetUnsafeStateAsync()
    {
        InitializeProjectorsIfNeeded();

        // Always get unsafe state
        return GetStateAsync(canGetUnsafeState: true);
    }

    private void InitializeProjectorsIfNeeded()
    {
        if (_singleStateAccessor == null)
        {
            var init = _types.GenerateInitialPayload(_projectorName);
            if (!init.IsSuccess)
            {
                throw init.GetException();
            }

            var initialPayload = init.GetValue();

            if (initialPayload is IDualStateAccessor)
            {
                // The payload already implements IDualStateAccessor, use it directly
                _singleStateAccessor = initialPayload;
            }
            else
            {
                // Wrap traditional projections in DualStateProjectionWrapper via factory
                _singleStateAccessor = DualStateProjectionWrapperFactory.Create(
                    initialPayload,
                    _projectorName,
                    _types,
                    _jsonOptions);

                if (_singleStateAccessor == null)
                {
                    throw new InvalidOperationException($"Failed to create wrapper for projector {_projectorName}");
                }
            }
        }
    }




    private SortableUniqueId GetSafeWindowThreshold()
    {
        var effectiveWindow = _options.SafeWindowMs;
        if (_options.EnableDynamicSafeWindow)
        {
            var extraEma = GetDecayedObservedLagMs();
            var extraMax = GetDecayedMaxLagMs();
            var extra = Math.Min(Math.Max(extraEma, extraMax), _options.MaxExtraSafeWindowMs);
            effectiveWindow = (int)Math.Max(0, Math.Min(int.MaxValue, (long)_options.SafeWindowMs + (long)extra));
        }
        var threshold = DateTime.UtcNow.AddMilliseconds(-effectiveWindow);
        _logger.LogDebug(
            "[SafeWindow] projector={ProjectorName} baseMs={BaseMs} effectiveMs={EffectiveMs} emaLagMs={EmaLagMs:F1} maxLagMs={MaxLagMs:F1} now={Now:O} threshold={Threshold:O}",
            _projectorName,
            _options.SafeWindowMs,
            effectiveWindow,
            _observedLagMs,
            _maxLagMs,
            DateTime.UtcNow,
            threshold);
        return new SortableUniqueId(SortableUniqueId.Generate(threshold, Guid.Empty));
    }

    /// <summary>
    ///     Public helper to obtain current safe window threshold without triggering state mutation.
    ///     (Primarily used to supply query context metadata.)
    /// </summary>
    public SortableUniqueId PeekCurrentSafeWindowThreshold() => GetSafeWindowThreshold();

    private void UpdateObservedLag(IReadOnlyList<Event> events)
    {
        var now = DateTime.UtcNow;
        // Representative lag: capped max of the batch
        double batchMax = 0;
        foreach (var ev in events)
        {
            var ts = new SortableUniqueId(ev.SortableUniqueIdValue).GetDateTime();
            var lagMs = (now - ts).TotalMilliseconds;
            if (lagMs > batchMax) batchMax = lagMs;
        }
        // Clamp to [0, MaxExtra]
        batchMax = Math.Max(0, Math.Min(batchMax, _options.MaxExtraSafeWindowMs));

        // Apply decay and update EMA
        var decayedEma = GetDecayedObservedLagMs();
        var alpha = Math.Clamp(_options.LagEmaAlpha, 0.01, 1.0);
        _observedLagMs = alpha * batchMax + (1 - alpha) * decayedEma;
        _lastLagUpdateUtc = now;

        // Update decayed running max: max(current decayed max, batchMax)
        var decayedMax = GetDecayedMaxLagMs();
        _maxLagMs = Math.Max(decayedMax, batchMax);
        _lastMaxUpdateUtc = now;
    }

    private double GetDecayedObservedLagMs()
    {
        if (_lastLagUpdateUtc == DateTime.MinValue || _observedLagMs <= 0)
        {
            return Math.Max(0, _observedLagMs);
        }
        var now = DateTime.UtcNow;
        var seconds = Math.Max(0, (now - _lastLagUpdateUtc).TotalSeconds);
        var decay = Math.Clamp(_options.LagDecayPerSecond, 0.5, 1.0);
        var factor = Math.Pow(decay, seconds);
        return Math.Max(0, _observedLagMs * factor);
    }

    private double GetDecayedMaxLagMs()
    {
        if (_lastMaxUpdateUtc == DateTime.MinValue || _maxLagMs <= 0)
        {
            return Math.Max(0, _maxLagMs);
        }
        var now = DateTime.UtcNow;
        var seconds = Math.Max(0, (now - _lastMaxUpdateUtc).TotalSeconds);
        var decay = Math.Clamp(_options.LagDecayPerSecond, 0.5, 1.0);
        var factor = Math.Pow(decay, seconds);
        return Math.Max(0, _maxLagMs * factor);
    }

    private ProjectionHeadStatus CreateProjectionHeadStatus(
        string projectorVersion,
        ProjectionPosition current,
        ProjectionPosition consistent)
    {
        var pendingUnsafeEventCount = Math.Max(0, current.EventVersion - consistent.EventVersion);
        var isCatchUpInProgress = !_isCatchedUp;

        return new ProjectionHeadStatus(
            _projectorName,
            projectorVersion,
            current,
            consistent,
            new ProjectionCatchUpStatus(
                isCatchUpInProgress,
                isCatchUpInProgress ? consistent.LastSortableUniqueId : null,
                isCatchUpInProgress ? current.LastSortableUniqueId : null,
                pendingUnsafeEventCount));
    }

    private static ProjectionPosition ToProjectionPosition(MultiProjectionState state) =>
        new(
            state.Version,
            ProjectionHeadStatusUtilities.NormalizeSortableUniqueId(state.LastSortableUniqueId));

}
