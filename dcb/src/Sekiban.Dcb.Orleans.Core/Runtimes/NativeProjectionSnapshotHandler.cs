using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ResultBoxes;
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
    private readonly ILogger _logger;

    public NativeProjectionSnapshotHandler(
        JsonSerializerOptions jsonOptions,
        GeneralMultiProjectionActor actor,
        ILogger logger)
    {
        _jsonOptions = jsonOptions;
        _actor = actor;
        _logger = logger;
    }

    public async Task<ResultBox<byte[]>> GetSnapshotBytesAsync(bool canGetUnsafeState = true)
    {
        try
        {
            var snapshotResult = await _actor.GetSnapshotAsync(canGetUnsafeState);
            if (!snapshotResult.IsSuccess)
            {
                return ResultBox.Error<byte[]>(snapshotResult.GetException());
            }

            var envelope = snapshotResult.GetValue();

            // Serialize directly into the final byte[] to avoid MemoryStream->ToArray double allocation.
            var snapshotBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, _jsonOptions);
            return ResultBox.FromValue(snapshotBytes);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<byte[]>(ex);
        }
    }

    public async Task<ResultBox<bool>> RestoreSnapshotAsync(byte[] snapshotData)
    {
        try
        {
            var envelopeJson = DecompressSnapshotData(snapshotData);

            var envelope = JsonSerializer.Deserialize<SerializableMultiProjectionStateEnvelope>(
                envelopeJson, _jsonOptions)!;

            await _actor.SetSnapshotAsync(envelope);
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task<ResultBox<bool>> WriteSnapshotToStreamAsync(
        Stream target,
        bool canGetUnsafeState,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshotResult = await _actor.GetSnapshotAsync(canGetUnsafeState, cancellationToken);
            if (!snapshotResult.IsSuccess)
            {
                return ResultBox.Error<bool>(snapshotResult.GetException());
            }

            var envelope = snapshotResult.GetValue();
            await JsonSerializer.SerializeAsync(target, envelope, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    public long EstimateStateSizeBytes(bool includeUnsafeDetails)
    {
        try
        {
            var stateResult = _actor.GetStateAsync(canGetUnsafeState: includeUnsafeDetails).GetAwaiter().GetResult();
            if (!stateResult.IsSuccess) return 0;

            var payload = stateResult.GetValue().Payload;

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

    public byte[] RewriteSnapshotVersion(byte[] snapshotData, string newVersion)
    {
        var envelopeJson = DecompressSnapshotData(snapshotData);

        var envelope = JsonSerializer.Deserialize<SerializableMultiProjectionStateEnvelope>(
            envelopeJson, _jsonOptions)!;

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
                    o.PayloadLength));
        }
        else
        {
            throw new InvalidOperationException("Cannot rewrite version: envelope has no inline or offloaded state");
        }

        var modifiedJson = JsonSerializer.Serialize(modified, _jsonOptions);
        return Encoding.UTF8.GetBytes(modifiedJson);
    }

    /// <summary>
    ///     Auto-detect snapshot format: v9 (Gzip) or v10 (plain JSON)
    /// </summary>
    private static string DecompressSnapshotData(byte[] snapshotData)
    {
        if (snapshotData.Length >= 2 && snapshotData[0] == 0x1f && snapshotData[1] == 0x8b)
        {
            return GzipCompression.DecompressToString(snapshotData);
        }
        return Encoding.UTF8.GetString(snapshotData);
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
}
