using System.Text.Json;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Common;

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

    public async Task<SerializableQueryResult> ExecuteQueryAsync(
        SerializableQueryParameter query,
        int? safeVersion,
        string? safeThreshold,
        DateTime? safeThresholdTime,
        int? unsafeVersion)
    {
        var deserialized = await query.ToQueryAsync(_domainTypes);
        if (deserialized is not IQueryCommon typedQuery)
        {
            throw new InvalidOperationException(
                $"Deserialized query does not implement IQueryCommon: {deserialized.GetType().FullName}");
        }

        // A failed state fetch is a real failure — a faulted projection, or a version-resolution error — and must
        // surface, not be laundered into an empty successful result. Ordinary catch-up lag does NOT reach here: it
        // returns a successful (partial) state, so only genuine faults fail the query.
        var state = await _actor.GetStateAsync();
        var projectorProvider = () => Task.FromResult(state.Payload!);

        // A failed handler faults the query with its own failure rather than wrapping a null value as a success.
        var value = await _domainTypes.QueryTypes.ExecuteQueryAsync(
            typedQuery,
            projectorProvider,
            _serviceProvider,
            safeVersion,
            safeThreshold,
            safeThresholdTime,
            unsafeVersion) ?? throw NullQueryResult(typedQuery);
        var resultType = value.GetType().FullName ?? string.Empty;

        return await SerializableQueryResult.CreateFromAsync(
            new QueryResultGeneral(value, resultType, typedQuery),
            _jsonOptions);
    }

    public async Task<SerializableListQueryResult> ExecuteListQueryAsync(
        SerializableQueryParameter query,
        int? safeVersion,
        string? safeThreshold,
        DateTime? safeThresholdTime,
        int? unsafeVersion)
    {
        var deserialized = await query.ToQueryAsync(_domainTypes);
        if (deserialized is not IListQueryCommon listQuery)
        {
            throw new InvalidOperationException(
                $"Deserialized query does not implement IListQueryCommon: {deserialized.GetType().FullName}");
        }

        // Same as the single-query path: a faulted projection fails the list query with its fault, instead of an empty
        // TotalCount=0 success that reads exactly like "there is no data" — the #1075 masking.
        var state = await _actor.GetStateAsync();
        var projectorProvider = () => Task.FromResult(state.Payload!);

        var result = await _domainTypes.QueryTypes.ExecuteListQueryAsGeneralAsync(
            listQuery,
            projectorProvider,
            _serviceProvider,
            safeVersion,
            safeThreshold,
            safeThresholdTime,
            unsafeVersion) ?? throw NullQueryResult(listQuery);

        return await SerializableListQueryResult.CreateFromAsync(result, _jsonOptions);
    }

    // A query-types implementation that completes with null has produced no result; it never travels as a success.
    private static InvalidOperationException NullQueryResult(object query) =>
        new($"Query {query.GetType().FullName} completed without a result.");
}
