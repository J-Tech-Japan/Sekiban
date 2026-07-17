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
                // A failed state fetch is a real failure — a faulted projection, or a version-resolution error — and
                // must surface, not be laundered into an empty successful result. Ordinary catch-up lag does NOT reach
                // here: it returns a successful (partial) state, so only genuine faults fail the query.
                return ResultBox.Error<SerializableQueryResult>(stateResult.GetException());
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

            if (!result.IsSuccess)
            {
                // The handler failed. Previously this fell through and wrapped a null value as a success, which later
                // surfaced as a confusing cast error far from the cause. Return the real failure instead.
                return ResultBox.Error<SerializableQueryResult>(result.GetException());
            }

            var value = result.GetValue();
            var resultType = value?.GetType().FullName ?? string.Empty;

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
                // Same as the single-query path: a faulted projection fails the list query with its fault, instead of
                // an empty TotalCount=0 success that reads exactly like "there is no data" — the #1075 masking.
                return ResultBox.Error<SerializableListQueryResult>(stateResult.GetException());
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

            if (!result.IsSuccess)
            {
                return ResultBox.Error<SerializableListQueryResult>(result.GetException());
            }

            var serialized = await SerializableListQueryResult.CreateFromAsync(
                result.GetValue(), _jsonOptions);
            return ResultBox.FromValue(serialized);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializableListQueryResult>(ex);
        }
    }
}
