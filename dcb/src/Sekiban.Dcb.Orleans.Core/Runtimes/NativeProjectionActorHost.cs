using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Serialization;

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Native C# implementation of IProjectionActorHost.
///     Uses NativeMultiProjectionProjectionPrimitive to create the underlying actor via the
///     primitive/accumulator abstraction.
///     Query execution and snapshot handling are delegated to focused helper classes.
/// </summary>
public class NativeProjectionActorHost : IProjectionActorHost
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly GeneralMultiProjectionActor _actor;
    private readonly string _projectorName;
    private readonly NativeProjectionQueryExecutor _queryExecutor;
    private readonly NativeProjectionSnapshotHandler _snapshotHandler;

    public NativeProjectionActorHost(
        DcbDomainTypes domainTypes,
        IServiceProvider serviceProvider,
        NativeMultiProjectionProjectionPrimitive primitive,
        string projectorName,
        GeneralMultiProjectionActorOptions? options,
        ILogger? logger)
    {
        _domainTypes = domainTypes;
        _projectorName = projectorName;
        var resolvedLogger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        var jsonOptions = domainTypes.JsonSerializerOptions;

        var projectorVersion = domainTypes.MultiProjectorTypes.GetProjectorVersion(projectorName);
        var version = projectorVersion.IsSuccess ? projectorVersion.GetValue() : "unknown";

        var accumulator = primitive.CreateNativeAccumulator(projectorName, version, options, logger);
        _actor = accumulator.GetActor();

        _queryExecutor = new NativeProjectionQueryExecutor(domainTypes, jsonOptions, serviceProvider, _actor);
        _snapshotHandler = new NativeProjectionSnapshotHandler(jsonOptions, _actor, resolvedLogger);
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

    public Task<ResultBox<byte[]>> GetSnapshotBytesAsync(bool canGetUnsafeState = true)
    {
        return _snapshotHandler.GetSnapshotBytesAsync(canGetUnsafeState);
    }

    public Task<ResultBox<bool>> WriteSnapshotToStreamAsync(
        Stream target,
        bool canGetUnsafeState,
        CancellationToken cancellationToken)
    {
        return _snapshotHandler.WriteSnapshotToStreamAsync(target, canGetUnsafeState, cancellationToken);
    }

    public Task<ResultBox<bool>> RestoreSnapshotAsync(byte[] snapshotData)
    {
        return _snapshotHandler.RestoreSnapshotAsync(snapshotData);
    }

    public Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
        SerializableQueryParameter query,
        int? safeVersion,
        string? safeThreshold,
        DateTime? safeThresholdTime,
        int? unsafeVersion)
    {
        return _queryExecutor.ExecuteQueryAsync(query, safeVersion, safeThreshold, safeThresholdTime, unsafeVersion);
    }

    public Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(
        SerializableQueryParameter query,
        int? safeVersion,
        string? safeThreshold,
        DateTime? safeThresholdTime,
        int? unsafeVersion)
    {
        return _queryExecutor.ExecuteListQueryAsync(query, safeVersion, safeThreshold, safeThresholdTime, unsafeVersion);
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
        return _snapshotHandler.EstimateStateSizeBytes(includeUnsafeDetails);
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
        return _snapshotHandler.RewriteSnapshotVersion(snapshotData, newVersion);
    }
}
