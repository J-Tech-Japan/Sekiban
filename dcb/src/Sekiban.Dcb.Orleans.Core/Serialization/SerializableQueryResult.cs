using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using Orleans.CodeGeneration;
using Sekiban.Dcb;
using Sekiban.Dcb.Queries;

namespace Sekiban.Dcb.Orleans.Serialization;

[GenerateSerializer]
public sealed record SerializableQueryResult
{
    [Id(0)]
    public string ResultTypeName { get; init; } = string.Empty;

    [Id(1)]
    public string QueryTypeName { get; init; } = string.Empty;

    [Id(2)]
    public byte[] CompressedResultJson { get; init; } = Array.Empty<byte>();

    [Id(3)]
    public byte[] CompressedQueryJson { get; init; } = Array.Empty<byte>();

    [Id(4)]
    public string ResultAssemblyVersion { get; init; } = string.Empty;

    /// <summary>
    ///     Indicates whether the projection is still catching up from the event store.
    ///     When true, the result may be incomplete or stale.
    /// </summary>
    [Id(5)]
    public bool IsCatchUpInProgress { get; init; }

    public SerializableQueryResult() { }

    private SerializableQueryResult(
        string resultTypeName,
        string queryTypeName,
        byte[] compressedResultJson,
        byte[] compressedQueryJson,
        string resultAssemblyVersion)
    {
        ResultTypeName = resultTypeName;
        QueryTypeName = queryTypeName;
        CompressedResultJson = compressedResultJson;
        CompressedQueryJson = compressedQueryJson;
        ResultAssemblyVersion = resultAssemblyVersion;
    }

    public static async Task<SerializableQueryResult> CreateFromAsync(
        QueryResultGeneral result,
        JsonSerializerOptions options)
    {
        var queryType = result.Query.GetType();
        var resultTypeName = result.ResultType;

        Type? resultType = result.Value?.GetType();
        if (resultType == null && !string.IsNullOrWhiteSpace(result.ResultType))
        {
            resultType = Type.GetType(result.ResultType, throwOnError: false);
        }

        var resultAssemblyVersion = resultType?.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        byte[] resultJson = Array.Empty<byte>();
        if (result.Value != null && resultType != null)
        {
            try
            {
                resultJson = JsonSerializer.SerializeToUtf8Bytes(result.Value, resultType, options);
            }
            catch (NotSupportedException)
            {
                resultJson = JsonSerializer.SerializeToUtf8Bytes(result.Value, resultType);
            }
        }

        byte[] queryJson;
        try
        {
            queryJson = JsonSerializer.SerializeToUtf8Bytes(result.Query, queryType, options);
        }
        catch (NotSupportedException)
        {
            queryJson = JsonSerializer.SerializeToUtf8Bytes(result.Query, queryType);
        }

        var compressedResultJson = await CompressAsync(resultJson);
        var compressedQueryJson = await CompressAsync(queryJson);

        return new SerializableQueryResult(
            resultTypeName,
            queryType.AssemblyQualifiedName ?? queryType.FullName ?? queryType.Name,
            compressedResultJson,
            compressedQueryJson,
            resultAssemblyVersion);
    }

    /// <summary>Serializes a query result value together with the query that produced it.</summary>
    public static async Task<SerializableQueryResult> CreateFromResultBoxAsync(
        object result,
        IQueryCommon originalQuery,
        JsonSerializerOptions options)
    {
        var resultTypeName = result?.GetType().FullName ?? string.Empty;
        var queryResultGeneral = new QueryResultGeneral(result!, resultTypeName, originalQuery);
        return await CreateFromAsync(queryResultGeneral, options);
    }

    /// <summary>
    ///     Deserializes the query result. An unknown query or result type and a query that does not deserialize fault the
    ///     returned task with that failure; any other deserialization failure is reported as an
    ///     <see cref="InvalidOperationException" /> wrapping it.
    /// </summary>
    public async Task<QueryResultGeneral> ToQueryResultAsync(DcbDomainTypes domainTypes)
    {
        (QueryResultGeneral? Result, Exception? Failure) outcome;
        try
        {
            outcome = await ReadQueryResultAsync(domainTypes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to deserialize query result: {ResultTypeName}", ex);
        }

        return outcome.Failure is null ? outcome.Result! : throw outcome.Failure;
    }

    private async Task<(QueryResultGeneral? Result, Exception? Failure)> ReadQueryResultAsync(DcbDomainTypes domainTypes)
    {
        var queryType = SerializableQueryParameter.TryResolveType(QueryTypeName, domainTypes, out var queryTypeFailure);
        if (queryType is null)
        {
            return (null, queryTypeFailure);
        }

        IQueryCommon? query = null;
        if (CompressedQueryJson.Length > 0)
        {
            var decompressedQueryJson = await DecompressAsync(CompressedQueryJson);
            try
            {
                query = JsonSerializer.Deserialize(
                    decompressedQueryJson,
                    queryType,
                    domainTypes.JsonSerializerOptions) as IQueryCommon;
            }
            catch (NotSupportedException)
            {
                query = JsonSerializer.Deserialize(decompressedQueryJson, queryType) as IQueryCommon;
            }
        }

        if (query == null)
        {
            return (null, new InvalidOperationException($"Failed to deserialize query: {QueryTypeName}"));
        }

        object? resultValue = null;
        Type? resultType = null;
        if (!string.IsNullOrWhiteSpace(ResultTypeName))
        {
            resultType = SerializableQueryParameter.TryResolveType(ResultTypeName, domainTypes, out var resultTypeFailure);
            if (resultType is null)
            {
                return (null, resultTypeFailure);
            }
        }

        if (CompressedResultJson.Length > 0)
        {
            if (resultType == null)
            {
                return (null, new InvalidOperationException(
                    $"Result type not specified for non-empty payload: {ResultTypeName}"));
            }

            var decompressedResultJson = await DecompressAsync(CompressedResultJson);
            try
            {
                resultValue = JsonSerializer.Deserialize(
                    decompressedResultJson,
                    resultType,
                    domainTypes.JsonSerializerOptions);
            }
            catch (NotSupportedException)
            {
                resultValue = JsonSerializer.Deserialize(decompressedResultJson, resultType);
            }
        }

        return (new QueryResultGeneral(resultValue ?? null!, ResultTypeName, query), null);
    }

    /// <summary>Deserializes the query result and returns its value.</summary>
    public async Task<object> ToResultBoxAsync(DcbDomainTypes domainTypes) =>
        (await ToQueryResultAsync(domainTypes)).Value;

    private static async Task<byte[]> CompressAsync(byte[] data)
    {
        if (data.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var memoryStream = new MemoryStream();
        using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Fastest))
        {
            await gzipStream.WriteAsync(data);
        }
        return memoryStream.ToArray();
    }

    private static async Task<byte[]> DecompressAsync(byte[] compressedData)
    {
        if (compressedData.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var compressedStream = new MemoryStream(compressedData);
        using var decompressedStream = new MemoryStream();
        using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
        {
            await gzipStream.CopyToAsync(decompressedStream);
        }
        return decompressedStream.ToArray();
    }
}
