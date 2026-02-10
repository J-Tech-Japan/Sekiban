using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Queries;

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Handles query execution for NativeProjectionActorHost.
///     Encapsulates the domain-specific query deserialization and execution logic.
/// </summary>
internal class NativeProjectionQueryExecutor
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IServiceProvider _serviceProvider;
    private readonly GeneralMultiProjectionActor _actor;

    public NativeProjectionQueryExecutor(
        DcbDomainTypes domainTypes,
        JsonSerializerOptions jsonOptions,
        IServiceProvider serviceProvider,
        GeneralMultiProjectionActor actor)
    {
        _domainTypes = domainTypes;
        _jsonOptions = jsonOptions;
        _serviceProvider = serviceProvider;
        _actor = actor;
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
}
