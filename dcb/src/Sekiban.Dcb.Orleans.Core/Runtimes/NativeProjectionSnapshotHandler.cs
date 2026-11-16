using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Handles snapshot serialization, restoration, rewriting, and size estimation
///     for NativeProjectionActorHost.
/// </summary>
internal class NativeProjectionSnapshotHandler
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly GeneralMultiProjectionActor _actor;
    private readonly IBlobStorageSnapshotAccessor? _blobAccessor;
    private readonly ISnapshotPayloadBufferProvider? _payloadBufferProvider;
    private readonly ILogger _logger;

    public NativeProjectionSnapshotHandler(
        JsonSerializerOptions jsonOptions,
        GeneralMultiProjectionActor actor,
        IBlobStorageSnapshotAccessor? blobAccessor,
        ILogger logger,
        ISnapshotPayloadBufferProvider? payloadBufferProvider = null)
    {
        _jsonOptions = jsonOptions;
        _actor = actor;
        _blobAccessor = blobAccessor;
        _payloadBufferProvider = payloadBufferProvider;
        _logger = logger;
    }

    public async Task<bool> RestoreSnapshotFromStreamAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        var envelope = await DeserializeEnvelopeAsync(source, cancellationToken);
        if (envelope is null)
        {
            throw new InvalidOperationException("Snapshot stream deserialized to null envelope.");
        }

        await using var resolvedSnapshot = await SnapshotEnvelopeResolver.ResolveForRestoreAsync(
                envelope,
                _blobAccessor,
                cancellationToken)
            .ConfigureAwait(false);

        // The resolver-owned stream stays open through the actor and optional per-projector streaming registry.
        // Do not publish anything from the host until this await succeeds.
        await _actor.SetResolvedSnapshotAsync(resolvedSnapshot, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> WriteSnapshotToStreamAsync(
        Stream target,
        bool canGetUnsafeState,
        CancellationToken cancellationToken)
    {
        var envelope = await _actor.GetSnapshotAsync(canGetUnsafeState, cancellationToken);
        await JsonSerializer.SerializeAsync(target, envelope, _jsonOptions, cancellationToken);
        return true;
    }

    public async Task<bool> WriteSnapshotForPersistenceToStreamAsync(
        Stream target,
        bool canGetUnsafeState,
        int offloadThresholdBytes,
        CancellationToken cancellationToken)
    {
        if (!canGetUnsafeState)
        {
            try
            {
                _actor.ForcePromoteBufferedEvents();
            }
            catch
            {
                // Best effort to align persistence snapshots with the current safe checkpoint.
            }
        }

        // Prefer the stream-first persistence path only when offload is actually possible:
        // both a blob accessor and a positive offload threshold must be configured, and a
        // spillable payload buffer provider must be registered. For inline-only
        // configurations (no blob accessor) the legacy byte[] path is still the fastest
        // because it avoids the buffer allocation + rehydrate copy that stream-first does
        // for sub-threshold payloads.
        var canStreamFirst = _payloadBufferProvider is not null
            && _blobAccessor is not null
            && offloadThresholdBytes > 0;

        var envelope = canStreamFirst
            ? await _actor.BuildSnapshotEnvelopeStreamFirstAsync(
                _payloadBufferProvider,
                canGetUnsafeState,
                _blobAccessor,
                offloadThresholdBytes,
                cancellationToken)
            : await _actor.BuildSnapshotEnvelopeAsync(
                canGetUnsafeState,
                _blobAccessor,
                offloadThresholdBytes,
                cancellationToken);
        await JsonSerializer.SerializeAsync(target, envelope, _jsonOptions, cancellationToken);
        return true;
    }

    public long EstimateStateSizeBytes(bool includeUnsafeDetails)
    {
        try
        {
            // An unavailable state estimates as zero without a warning; anything else thrown here is logged below.
            var stateRead = _actor.GetStateAsync(canGetUnsafeState: includeUnsafeDetails);
            MultiProjectionState state;
            try
            {
                state = stateRead.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                return 0;
            }

            var payload = state.Payload;

            var payloadType = payload.GetType();
            var stateProp = payloadType.GetProperty("State");
            if (stateProp != null)
            {
                var stateObj = stateProp.GetValue(payload);
                if (stateObj != null && stateObj.GetType().Name.StartsWith("SafeUnsafeProjectionState"))
                {
                    return EstimateSafeUnsafeProjectionStateSize(stateObj, includeUnsafeDetails);
                }
            }

            var defJson = JsonSerializer.Serialize(payload, payload.GetType(), _jsonOptions);
            return Encoding.UTF8.GetByteCount(defJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[NativeProjectionSnapshotHandler] Error estimating state size");
            return 0;
        }
    }

    public async Task<bool> RewriteSnapshotVersionAsync(
        Stream source,
        Stream target,
        string newVersion,
        CancellationToken cancellationToken)
    {
        var envelope = await DeserializeEnvelopeAsync(source, cancellationToken);
        if (envelope is null)
        {
            throw new InvalidOperationException("Snapshot stream deserialized to null envelope.");
        }

        SerializableMultiProjectionStateEnvelope modified;
        if (!envelope.IsOffloaded && envelope.InlineState != null)
        {
            var s = envelope.InlineState;
            modified = new SerializableMultiProjectionStateEnvelope(
                false,
                SerializableMultiProjectionState.FromBytes(
                    s.GetPayloadBytes(), s.MultiProjectionPayloadType, s.ProjectorName, newVersion,
                    s.LastSortableUniqueId, s.LastEventId, s.Version, s.IsCatchedUp, s.IsSafeState,
                    s.OriginalSizeBytes, s.CompressedSizeBytes),
                null);
        }
        else if (envelope.OffloadedState != null)
        {
            var o = envelope.OffloadedState;
            modified = new SerializableMultiProjectionStateEnvelope(
                true,
                null,
                new SerializableMultiProjectionStateOffloaded(
                    o.OffloadKey, o.StorageProvider, o.MultiProjectionPayloadType, o.ProjectorName,
                    newVersion, o.LastSortableUniqueId, o.LastEventId, o.Version, o.IsCatchedUp, o.IsSafeState,
                    o.PayloadLength, o.OriginalSizeBytes, o.CompressedSizeBytes));
        }
        else
        {
            throw new InvalidOperationException("Cannot rewrite version: envelope has no inline or offloaded state");
        }

        await JsonSerializer.SerializeAsync(target, modified, _jsonOptions, cancellationToken);
        return true;
    }

    private long EstimateSafeUnsafeProjectionStateSize(object stateObj, bool includeUnsafeDetails)
    {
        var stateType = stateObj.GetType();
        var currentDataField = stateType.GetField("_currentData",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var safeBackupField = stateType.GetField("_safeBackup",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var currentData = currentDataField?.GetValue(stateObj) as System.Collections.IDictionary;
        var safeBackup = safeBackupField?.GetValue(stateObj) as System.Collections.IDictionary;

        var safeKeys = new List<string>();
        var unsafeKeys = new List<string>();

        if (currentData != null)
        {
            foreach (System.Collections.DictionaryEntry de in currentData)
            {
                safeKeys.Add(de.Key?.ToString() ?? string.Empty);
            }
        }
        if (safeBackup != null)
        {
            var backupKeys = new HashSet<string>();
            foreach (System.Collections.DictionaryEntry de in safeBackup)
            {
                backupKeys.Add(de.Key?.ToString() ?? string.Empty);
            }
            unsafeKeys.AddRange(backupKeys);
            safeKeys = safeKeys.Where(k => !backupKeys.Contains(k)).ToList();
        }

        object dto = includeUnsafeDetails
            ? new { safeKeys, unsafeKeys }
            : new { safeKeys };
        var json = JsonSerializer.Serialize(dto, _jsonOptions);
        return Encoding.UTF8.GetByteCount(json);
    }

    private async Task<SerializableMultiProjectionStateEnvelope?> DeserializeEnvelopeAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        return await PossiblyGzippedJsonSerializer.DeserializeAsync<SerializableMultiProjectionStateEnvelope>(
            source,
            _jsonOptions,
            cancellationToken);
    }
}
