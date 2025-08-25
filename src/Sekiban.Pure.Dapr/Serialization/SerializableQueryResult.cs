using ResultBoxes;
using Sekiban.Pure.Query;
using System.IO.Compression;
using System.Text.Json;
namespace Sekiban.Pure.Dapr.Serialization;

[Serializable]
public record SerializableQueryResult
{
    public string ResultTypeName { get; init; } = string.Empty;

    public string QueryTypeName { get; init; } = string.Empty;

    public byte[] CompressedResultJson { get; init; } = Array.Empty<byte>();

    public byte[] CompressedQueryJson { get; init; } = Array.Empty<byte>();

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
        var resultType = result.Value.GetType();
        var queryType = result.Query.GetType();
        var resultAssemblyVersion = resultType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        // Use default options if the provided options don't support the type
        byte[] resultJson;
        try
        {
            resultJson = JsonSerializer.SerializeToUtf8Bytes(result.Value, resultType, options);
        }
        catch (NotSupportedException)
        {
            resultJson = JsonSerializer.SerializeToUtf8Bytes(result.Value, resultType);
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
            result.ResultType,
            queryType.FullName ?? queryType.Name,
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
        var resultType = value.GetType();
        var queryResultGeneral = new QueryResultGeneral(value, resultType.FullName ?? resultType.Name, originalQuery);
        var serializable = await CreateFromAsync(queryResultGeneral, options);

        return ResultBox<SerializableQueryResult>.FromValue(serializable);
    }

    public async Task<ResultBox<QueryResultGeneral>> ToQueryResultAsync(SekibanDomainTypes domainTypes)
    {
        try
        {
            Type? resultType = null;
            try
            {
                // Try to get type directly from Type.GetType
                resultType = Type.GetType(ResultTypeName);

                // If that fails, search in all loaded assemblies
                if (resultType == null)
                {
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        resultType = assembly.GetType(ResultTypeName);
                        if (resultType != null) break;
                    }
                }
                if (resultType == null)
                {
                    return ResultBox<QueryResultGeneral>.FromException(
                        new InvalidOperationException($"Result type not found: {ResultTypeName}"));
                }
            }
            catch (Exception ex)
            {
                return ResultBox<QueryResultGeneral>.FromException(
                    new InvalidOperationException($"Failed to get result type: {ResultTypeName}", ex));
            }

            Type? queryType = null;
            try
            {
                // Try to get type directly from Type.GetType
                queryType = Type.GetType(QueryTypeName);

                // If that fails, search in all loaded assemblies
                if (queryType == null)
                {
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        queryType = assembly.GetType(QueryTypeName);
                        if (queryType != null) break;
                    }
                }
                if (queryType == null)
                {
                    return ResultBox<QueryResultGeneral>.FromException(
                        new InvalidOperationException($"Query type not found: {QueryTypeName}"));
                }
            }
            catch (Exception ex)
            {
                return ResultBox<QueryResultGeneral>.FromException(
                    new InvalidOperationException($"Failed to get query type: {QueryTypeName}", ex));
            }

            object? result = null;
            if (CompressedResultJson.Length > 0)
            {
                var decompressedJson = await DecompressAsync(CompressedResultJson);
                try
                {
                    result = JsonSerializer.Deserialize(
                        decompressedJson,
                        resultType,
                        domainTypes.JsonSerializerOptions);
                }
                catch (NotSupportedException)
                {
                    // Fallback to default options
                    result = JsonSerializer.Deserialize(decompressedJson, resultType);
                }

                if (result == null)
                {
                    return ResultBox<QueryResultGeneral>.FromException(
                        new InvalidOperationException($"Failed to deserialize result: {ResultTypeName}"));
                }
            } else
            {
                return ResultBox<QueryResultGeneral>.FromException(
                    new InvalidOperationException("No result data to deserialize"));
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
                    // Fallback to default options
                    query = JsonSerializer.Deserialize(decompressedQueryJson, queryType) as IQueryCommon;
                }

                if (query == null)
                {
                    return ResultBox<QueryResultGeneral>.FromException(
                        new InvalidOperationException($"Failed to deserialize query: {QueryTypeName}"));
                }
            } else
            {
                return ResultBox<QueryResultGeneral>.FromException(
                    new InvalidOperationException("No query data to deserialize"));
            }

            var queryResultGeneral = new QueryResultGeneral(result, ResultTypeName, query);
            return ResultBox<QueryResultGeneral>.FromValue(queryResultGeneral);
        }
        catch (Exception ex)
        {
            return ResultBox<QueryResultGeneral>.FromException(
                new InvalidOperationException($"Failed to convert to query result: {ResultTypeName}", ex));
        }
    }

    public async Task<ResultBox<object>> ToResultBoxAsync(SekibanDomainTypes domainTypes)
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
