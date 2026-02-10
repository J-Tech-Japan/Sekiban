using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Queries;

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Native C# implementation of IProjectionRuntime.
///     Delegates to DcbDomainTypes for projection management, event application, and query execution.
/// </summary>
public class NativeProjectionRuntime : IProjectionRuntime
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly ICoreMultiProjectorTypes _multiProjectorTypes;
    private readonly ICoreQueryTypes _queryTypes;

    public NativeProjectionRuntime(DcbDomainTypes domainTypes)
    {
        _domainTypes = domainTypes;
        _multiProjectorTypes = domainTypes.MultiProjectorTypes;
        _queryTypes = domainTypes.QueryTypes;
    }

    public ResultBox<IProjectionState> GenerateInitialState(string projectorName)
    {
        var payloadResult = _multiProjectorTypes.GenerateInitialPayload(projectorName);
        if (!payloadResult.IsSuccess)
        {
            return ResultBox.Error<IProjectionState>(payloadResult.GetException());
        }

        var payload = payloadResult.GetValue();
        var wrapper = DualStateWrapperHelper.CreateWrapper(
            payload, projectorName, _multiProjectorTypes, _domainTypes);
        if (wrapper is not IDualStateAccessor accessor)
        {
            return ResultBox.Error<IProjectionState>(
                new InvalidOperationException(
                    $"Failed to create DualStateProjectionWrapper for '{projectorName}'"));
        }

        return ResultBox.FromValue<IProjectionState>(
            NativeProjectionState.FromDualStateAccessor(accessor));
    }

    public ResultBox<string> GetProjectorVersion(string projectorName) =>
        _multiProjectorTypes.GetProjectorVersion(projectorName);

    public IReadOnlyList<string> GetAllProjectorNames() =>
        _multiProjectorTypes.GetAllProjectorNames();

    public ResultBox<IProjectionState> ApplyEvent(
        string projectorName,
        IProjectionState currentState,
        Event ev,
        string safeWindowThreshold)
    {
        if (currentState is not NativeProjectionState nativeState)
        {
            return ResultBox.Error<IProjectionState>(
                new InvalidOperationException("State must be a NativeProjectionState"));
        }

        var payload = nativeState.Payload;

        if (payload is IDualStateAccessor accessor)
        {
            return DualStateWrapperHelper.ApplyEvent(
                accessor, ev, safeWindowThreshold, _domainTypes);
        }

        return ApplyEventViaProjectorTypes(
            projectorName, nativeState, payload, ev, safeWindowThreshold);
    }

    public ResultBox<IProjectionState> ApplyEvents(
        string projectorName,
        IProjectionState currentState,
        IReadOnlyList<Event> events,
        string safeWindowThreshold)
    {
        var state = currentState;
        foreach (var ev in events)
        {
            var result = ApplyEvent(projectorName, state, ev, safeWindowThreshold);
            if (!result.IsSuccess)
            {
                return result;
            }
            state = result.GetValue();
        }
        return ResultBox.FromValue(state);
    }

    public async Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
        string projectorName,
        IProjectionState state,
        SerializableQueryParameter query,
        IServiceProvider serviceProvider)
    {
        if (state is not NativeProjectionState nativeState)
        {
            return ResultBox.Error<SerializableQueryResult>(
                new InvalidOperationException("State must be a NativeProjectionState"));
        }

        var queryResult = await query.ToQueryAsync(_domainTypes);
        if (!queryResult.IsSuccess)
        {
            return ResultBox.Error<SerializableQueryResult>(queryResult.GetException());
        }

        var queryObj = queryResult.GetValue();
        if (queryObj is not IQueryCommon queryCommon)
        {
            return ResultBox.Error<SerializableQueryResult>(
                new InvalidOperationException("Deserialized query is not IQueryCommon"));
        }

        if (nativeState.GetUnsafePayload() is not IMultiProjectionPayload payload)
        {
            return ResultBox.Error<SerializableQueryResult>(
                new InvalidOperationException("Unsafe payload is null or not IMultiProjectionPayload"));
        }

        var projectorProvider = () => Task.FromResult(ResultBox.FromValue(payload));

        var result = await _queryTypes.ExecuteQueryAsync(
            queryCommon,
            projectorProvider,
            serviceProvider,
            nativeState.SafeVersion,
            nativeState.SafeLastSortableUniqueId,
            null,
            nativeState.UnsafeVersion);

        return await SerializableQueryResult.CreateFromResultBoxAsync(
            result,
            queryCommon,
            _domainTypes.JsonSerializerOptions);
    }

    public async Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(
        string projectorName,
        IProjectionState state,
        SerializableQueryParameter query,
        IServiceProvider serviceProvider)
    {
        if (state is not NativeProjectionState nativeState)
        {
            return ResultBox.Error<SerializableListQueryResult>(
                new InvalidOperationException("State must be a NativeProjectionState"));
        }

        var queryResult = await query.ToQueryAsync(_domainTypes);
        if (!queryResult.IsSuccess)
        {
            return ResultBox.Error<SerializableListQueryResult>(queryResult.GetException());
        }

        var queryObj = queryResult.GetValue();
        if (queryObj is not IListQueryCommon listQuery)
        {
            return ResultBox.Error<SerializableListQueryResult>(
                new InvalidOperationException("Deserialized query is not IListQueryCommon"));
        }

        if (nativeState.GetUnsafePayload() is not IMultiProjectionPayload payload)
        {
            return ResultBox.Error<SerializableListQueryResult>(
                new InvalidOperationException("Unsafe payload is null or not IMultiProjectionPayload"));
        }

        var projectorProvider = () => Task.FromResult(ResultBox.FromValue(payload));

        var result = await _queryTypes.ExecuteListQueryAsGeneralAsync(
            listQuery,
            projectorProvider,
            serviceProvider,
            nativeState.SafeVersion,
            nativeState.SafeLastSortableUniqueId,
            null,
            nativeState.UnsafeVersion);

        if (!result.IsSuccess)
        {
            return ResultBox.Error<SerializableListQueryResult>(result.GetException());
        }

        var general = result.GetValue();
        return ResultBox.FromValue(
            await SerializableListQueryResult.CreateFromAsync(
                general,
                _domainTypes.JsonSerializerOptions));
    }

    public ResultBox<byte[]> SerializeState(string projectorName, IProjectionState state)
    {
        if (state is not NativeProjectionState nativeState)
        {
            return ResultBox.Error<byte[]>(
                new InvalidOperationException("State must be a NativeProjectionState"));
        }

        var safeThreshold = nativeState.SafeLastSortableUniqueId
            ?? SortableUniqueId.MinValue.Value;

        var serResult = _multiProjectorTypes.Serialize(
            projectorName,
            _domainTypes,
            safeThreshold,
            nativeState.Payload);

        if (!serResult.IsSuccess)
        {
            return ResultBox.Error<byte[]>(serResult.GetException());
        }

        return ResultBox.FromValue(serResult.GetValue().Data);
    }

    public ResultBox<IProjectionState> DeserializeState(
        string projectorName,
        byte[] data,
        string safeWindowThreshold)
    {
        var payloadResult = _multiProjectorTypes.Deserialize(
            projectorName,
            _domainTypes,
            safeWindowThreshold,
            data);

        if (!payloadResult.IsSuccess)
        {
            return ResultBox.Error<IProjectionState>(payloadResult.GetException());
        }

        var payload = payloadResult.GetValue();
        var wrapper = DualStateWrapperHelper.CreateWrapper(
            payload, projectorName, _multiProjectorTypes, _domainTypes,
            isRestoredFromSnapshot: true);

        if (wrapper is IDualStateAccessor restoredAccessor)
        {
            return ResultBox.FromValue<IProjectionState>(
                NativeProjectionState.FromDualStateAccessor(restoredAccessor));
        }

        return ResultBox.FromValue<IProjectionState>(
            NativeProjectionState.FromInitialPayload(payload));
    }

    public ResultBox<string> ResolveProjectorName(IQueryCommon query) =>
        ResolveProjectorNameFromType(_queryTypes.GetMultiProjectorType(query));

    public ResultBox<string> ResolveProjectorName(IListQueryCommon query) =>
        ResolveProjectorNameFromType(_queryTypes.GetMultiProjectorType(query));

    private ResultBox<string> ResolveProjectorNameFromType(ResultBox<Type> typeResult)
    {
        if (!typeResult.IsSuccess)
        {
            return ResultBox.Error<string>(typeResult.GetException());
        }

        var projectorType = typeResult.GetValue();
        foreach (var name in _multiProjectorTypes.GetAllProjectorNames())
        {
            var ptResult = _multiProjectorTypes.GetProjectorType(name);
            if (ptResult.IsSuccess && ptResult.GetValue() == projectorType)
            {
                return ResultBox.FromValue(name);
            }
        }

        return ResultBox.Error<string>(
            new InvalidOperationException(
                $"No projector found for type '{projectorType.Name}'"));
    }

    private ResultBox<IProjectionState> ApplyEventViaProjectorTypes(
        string projectorName,
        NativeProjectionState nativeState,
        IMultiProjectionPayload payload,
        Event ev,
        string safeWindowThreshold)
    {
        var tags = ev.Tags
            .Select(tagString => _domainTypes.TagTypes.GetTag(tagString))
            .ToList();
        var projected = _multiProjectorTypes.Project(
            projectorName,
            payload,
            ev,
            tags,
            _domainTypes,
            new SortableUniqueId(safeWindowThreshold));

        if (!projected.IsSuccess)
        {
            return ResultBox.Error<IProjectionState>(projected.GetException());
        }

        var newPayload = projected.GetValue();
        return ResultBox.FromValue<IProjectionState>(
            new NativeProjectionState(
                newPayload,
                nativeState.SafeVersion,
                nativeState.UnsafeVersion + 1,
                nativeState.SafeLastSortableUniqueId,
                ev.SortableUniqueIdValue,
                ev.Id));
    }
}
