using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using Orleans.CodeGeneration;
using ResultBoxes;
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

    public static async Task<ResultBox<SerializableQueryResult>> CreateFromResultBoxAsync(
        ResultBox<object> resultBox,
        IQueryCommon originalQuery,
        JsonSerializerOptions options)
    {
        if (!resultBox.IsSuccess)
        {
            return ResultBox<SerializableQueryResult>.FromException(resultBox.GetException());
        }

        var value = resultBox.GetValue();
        var resultTypeName = value?.GetType().FullName ?? string.Empty;
        var queryResultGeneral = new QueryResultGeneral(value!, resultTypeName, originalQuery);
        var serializable = await CreateFromAsync(queryResultGeneral, options);

        return ResultBox<SerializableQueryResult>.FromValue(serializable);
    }

    public async Task<ResultBox<QueryResultGeneral>> ToQueryResultAsync(DcbDomainTypes domainTypes)
    {
        try
        {
            var queryTypeResult = SerializableQueryParameter.ResolveType(QueryTypeName, domainTypes);
            if (!queryTypeResult.IsSuccess)
            {
                return ResultBox<QueryResultGeneral>.FromException(queryTypeResult.GetException());
            }

            var queryType = queryTypeResult.GetValue();

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
                return ResultBox<QueryResultGeneral>.FromException(
                    new InvalidOperationException($"Failed to deserialize query: {QueryTypeName}"));
            }

            object? resultValue = null;
            Type? resultType = null;
            if (!string.IsNullOrWhiteSpace(ResultTypeName))
            {
                var resultTypeResult = SerializableQueryParameter.ResolveType(ResultTypeName, domainTypes);
                if (!resultTypeResult.IsSuccess)
                {
                    return ResultBox<QueryResultGeneral>.FromException(resultTypeResult.GetException());
                }
                resultType = resultTypeResult.GetValue();
            }

            if (CompressedResultJson.Length > 0)
            {
                if (resultType == null)
                {
                    return ResultBox<QueryResultGeneral>.FromException(
                        new InvalidOperationException(
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

            var queryResultGeneral = new QueryResultGeneral(resultValue ?? null!, ResultTypeName, query);
            return ResultBox<QueryResultGeneral>.FromValue(queryResultGeneral);
        }
        catch (Exception ex)
        {
            return ResultBox<QueryResultGeneral>.FromException(
                new InvalidOperationException($"Failed to deserialize query result: {ResultTypeName}", ex));
        }
    }

    public async Task<ResultBox<object>> ToResultBoxAsync(DcbDomainTypes domainTypes)
    {
        var queryResultBox = await ToQueryResultAsync(domainTypes);
        return queryResultBox.Conveyor(qr => ResultBox<object>.FromValue(qr.Value));
    }

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
