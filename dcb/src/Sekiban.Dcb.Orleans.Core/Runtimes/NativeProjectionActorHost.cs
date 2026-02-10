using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Native C# implementation of IProjectionActorHost.
///     Wraps GeneralMultiProjectionActor and hides all domain-specific dependencies
///     (DcbDomainTypes, JsonSerializerOptions, IServiceProvider) from the Grain.
/// </summary>
public class NativeProjectionActorHost : IProjectionActorHost
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IServiceProvider _serviceProvider;
    private readonly GeneralMultiProjectionActor _actor;
    private readonly string _projectorName;
    private readonly ILogger _logger;

    public NativeProjectionActorHost(
        DcbDomainTypes domainTypes,
        IServiceProvider serviceProvider,
        string projectorName,
        GeneralMultiProjectionActorOptions? options,
        ILogger? logger)
    {
        _domainTypes = domainTypes;
        _jsonOptions = domainTypes.JsonSerializerOptions;
        _serviceProvider = serviceProvider;
        _projectorName = projectorName;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        _actor = new GeneralMultiProjectionActor(domainTypes, projectorName, options, logger);
    }

    public Task AddSerializableEventsAsync(
        IReadOnlyList<SerializableEvent> events,
        bool finishedCatchUp = true)
    {
        return _actor.AddSerializableEventsAsync(events, finishedCatchUp);
    }

    public Task AddEventsFromCatchUpAsync(
        IReadOnlyList<Event> events,
        bool finishedCatchUp = true)
    {
        return _actor.AddEventsAsync(events, finishedCatchUp, EventSource.CatchUp);
    }

    public async Task<ResultBox<ProjectionStateMetadata>> GetStateMetadataAsync(bool includeUnsafe = true)
    {
        var unsafeResult = await _actor.GetStateAsync(canGetUnsafeState: true);
        var safeResult = await _actor.GetStateAsync(canGetUnsafeState: false);

        if (!safeResult.IsSuccess)
        {
            return ResultBox.Error<ProjectionStateMetadata>(safeResult.GetException());
        }

        var safeState = safeResult.GetValue();
        var versionResult = _domainTypes.MultiProjectorTypes.GetProjectorVersion(_projectorName);
        var projectorVersion = versionResult.IsSuccess ? versionResult.GetValue() : "unknown";

        int unsafeVersion = 0;
        string? unsafeLastSortableUniqueId = null;
        Guid? unsafeLastEventId = null;

        if (includeUnsafe && unsafeResult.IsSuccess)
        {
            var unsafeState = unsafeResult.GetValue();
            unsafeVersion = unsafeState.Version;
            unsafeLastSortableUniqueId = unsafeState.LastSortableUniqueId;
            unsafeLastEventId = unsafeState.LastEventId;
        }

        return ResultBox.FromValue(new ProjectionStateMetadata(
            ProjectorName: _projectorName,
            ProjectorVersion: projectorVersion,
            IsCatchedUp: safeState.IsCatchedUp,
            UnsafeVersion: unsafeVersion,
            UnsafeLastSortableUniqueId: unsafeLastSortableUniqueId,
            UnsafeLastEventId: unsafeLastEventId,
            SafeVersion: safeState.Version,
            SafeLastSortableUniqueId: safeState.LastSortableUniqueId));
    }

    public Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true)
    {
        return _actor.GetStateAsync(canGetUnsafeState);
    }

    public async Task<ResultBox<byte[]>> GetSnapshotBytesAsync(bool canGetUnsafeState = true)
    {
        var snapshotResult = await _actor.GetSnapshotAsync(canGetUnsafeState);
        if (!snapshotResult.IsSuccess)
        {
            return ResultBox.Error<byte[]>(snapshotResult.GetException());
        }

        var envelope = snapshotResult.GetValue();
        using var memoryStream = new MemoryStream();
        await JsonSerializer.SerializeAsync(memoryStream, envelope, _jsonOptions);
        return ResultBox.FromValue(memoryStream.ToArray());
    }

    public async Task<ResultBox<bool>> RestoreSnapshotAsync(byte[] snapshotData)
    {
        try
        {
            // Auto-detect format: v9 (Gzip) or v10 (plain JSON)
            string envelopeJson;
            if (snapshotData.Length >= 2 && snapshotData[0] == 0x1f && snapshotData[1] == 0x8b)
            {
                envelopeJson = GzipCompression.DecompressToString(snapshotData);
            }
            else
            {
                envelopeJson = Encoding.UTF8.GetString(snapshotData);
            }

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

    public async Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
        SerializableQueryParameter query,
        int? safeVersion,
        string? safeThreshold,
        DateTime? safeThresholdTime,
        int? unsafeVersion)
    {
        try
        {
            var queryBox = await query.ToQueryAsync(_domainTypes);
            if (!queryBox.IsSuccess)
            {
                return ResultBox.Error<SerializableQueryResult>(queryBox.GetException());
            }

            if (queryBox.GetValue() is not IQueryCommon typedQuery)
            {
                return ResultBox.Error<SerializableQueryResult>(
                    new InvalidOperationException(
                        $"Deserialized query does not implement IQueryCommon: {queryBox.GetValue().GetType().FullName}"));
            }

            var stateResult = await _actor.GetStateAsync();
            if (!stateResult.IsSuccess)
            {
                var emptyResult = await SerializableQueryResult.CreateFromAsync(
                    new QueryResultGeneral(null!, string.Empty, typedQuery),
                    _jsonOptions);
                return ResultBox.FromValue(emptyResult);
            }

            var state = stateResult.GetValue();
            var projectorProvider = () => Task.FromResult(ResultBox.FromValue(state.Payload!));

            var result = await _domainTypes.QueryTypes.ExecuteQueryAsync(
                typedQuery,
                projectorProvider,
                _serviceProvider,
                safeVersion,
                safeThreshold,
                safeThresholdTime,
                unsafeVersion);

            object? value = null;
            string resultType = string.Empty;

            if (result.IsSuccess)
            {
                value = result.GetValue();
                resultType = value?.GetType().FullName ?? string.Empty;
            }

            var serialized = await SerializableQueryResult.CreateFromAsync(
                new QueryResultGeneral(value ?? null!, resultType, typedQuery),
                _jsonOptions);
            return ResultBox.FromValue(serialized);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializableQueryResult>(ex);
        }
    }

    public async Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(
        SerializableQueryParameter query,
        int? safeVersion,
        string? safeThreshold,
        DateTime? safeThresholdTime,
        int? unsafeVersion)
    {
        try
        {
            var queryBox = await query.ToQueryAsync(_domainTypes);
            if (!queryBox.IsSuccess)
            {
                return ResultBox.Error<SerializableListQueryResult>(queryBox.GetException());
            }

            if (queryBox.GetValue() is not IListQueryCommon listQuery)
            {
                return ResultBox.Error<SerializableListQueryResult>(
                    new InvalidOperationException(
                        $"Deserialized query does not implement IListQueryCommon: {queryBox.GetValue().GetType().FullName}"));
            }

            var stateResult = await _actor.GetStateAsync();
            if (!stateResult.IsSuccess)
            {
                var emptyGeneral = new ListQueryResultGeneral(
                    0, 0, 0, 0, Array.Empty<object>(), string.Empty, listQuery);
                var emptyResult = await SerializableListQueryResult.CreateFromAsync(
                    emptyGeneral, _jsonOptions);
                return ResultBox.FromValue(emptyResult);
            }

            var state = stateResult.GetValue();
            var projectorProvider = () => Task.FromResult(ResultBox.FromValue(state.Payload!));

            var result = await _domainTypes.QueryTypes.ExecuteListQueryAsGeneralAsync(
                listQuery,
                projectorProvider,
                _serviceProvider,
                safeVersion,
                safeThreshold,
                safeThresholdTime,
                unsafeVersion);

            var general = result.IsSuccess
                ? result.GetValue()
                : new ListQueryResultGeneral(
                    0, 0, 0, 0, Array.Empty<object>(), string.Empty, listQuery);

            var serialized = await SerializableListQueryResult.CreateFromAsync(
                general, _jsonOptions);
            return ResultBox.FromValue(serialized);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializableListQueryResult>(ex);
        }
    }

    public void ForcePromoteBufferedEvents()
    {
        _actor.ForcePromoteBufferedEvents();
    }

    public void ForcePromoteAllBufferedEvents()
    {
        _actor.ForcePromoteAllBufferedEvents();
    }

    public Task<string> GetSafeLastSortableUniqueIdAsync()
    {
        return _actor.GetSafeLastSortableUniqueIdAsync();
    }

    public Task<bool> IsSortableUniqueIdReceivedAsync(string sortableUniqueId)
    {
        return _actor.IsSortableUniqueIdReceived(sortableUniqueId);
    }

    public long EstimateStateSizeBytes(bool includeUnsafeDetails)
    {
        try
        {
            var stateResult = _actor.GetStateAsync(canGetUnsafeState: includeUnsafeDetails).GetAwaiter().GetResult();
            if (!stateResult.IsSuccess) return 0;

            var payload = stateResult.GetValue().Payload;

            // Special handling for TagState-based projectors
            var payloadType = payload.GetType();
            var stateProp = payloadType.GetProperty("State");
            if (stateProp != null)
            {
                var stateObj = stateProp.GetValue(payload);
                if (stateObj != null && stateObj.GetType().Name.StartsWith("SafeUnsafeProjectionState"))
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

            var defJson = JsonSerializer.Serialize(payload, payload.GetType(), _jsonOptions);
            return Encoding.UTF8.GetByteCount(defJson);
        }
        catch
        {
            return 0;
        }
    }

    public string PeekCurrentSafeWindowThreshold()
    {
        return _actor.PeekCurrentSafeWindowThreshold().Value;
    }

    public string GetProjectorVersion()
    {
        var versionResult = _domainTypes.MultiProjectorTypes.GetProjectorVersion(_projectorName);
        return versionResult.IsSuccess ? versionResult.GetValue() : "unknown";
    }

    public byte[] RewriteSnapshotVersion(byte[] snapshotData, string newVersion)
    {
        // Auto-detect format: v9 (Gzip) or v10 (plain JSON)
        string envelopeJson;
        if (snapshotData.Length >= 2 && snapshotData[0] == 0x1f && snapshotData[1] == 0x8b)
        {
            envelopeJson = GzipCompression.DecompressToString(snapshotData);
        }
        else
        {
            envelopeJson = Encoding.UTF8.GetString(snapshotData);
        }

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
}
