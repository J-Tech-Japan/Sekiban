using ResultBoxes;
using Sekiban.Pure.Query;
using System.IO.Compression;
using System.Text.Json;
namespace Sekiban.Pure.Dapr.Serialization;

[Serializable]
public record SerializableQuery
{
    public string QueryTypeName { get; init; } = string.Empty;

    public byte[] CompressedQueryJson { get; init; } = Array.Empty<byte>();

    public string QueryAssemblyVersion { get; init; } = string.Empty;

    public SerializableQuery() { }

    private SerializableQuery(string queryTypeName, byte[] compressedQueryJson, string queryAssemblyVersion)
    {
        QueryTypeName = queryTypeName;
        CompressedQueryJson = compressedQueryJson;
        QueryAssemblyVersion = queryAssemblyVersion;
    }

    public static async Task<SerializableQuery> CreateFromAsync(IQueryCommon query, JsonSerializerOptions options)
    {
        var queryType = query.GetType();
        var queryAssemblyVersion = queryType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        // Use default options if the provided options don't support the type
        byte[] queryJson;
        try
        {
            queryJson = JsonSerializer.SerializeToUtf8Bytes(query, queryType, options);
        }
        catch (NotSupportedException)
        {
            // Fallback to default serialization options for types not in source generation
            queryJson = JsonSerializer.SerializeToUtf8Bytes(query, queryType);
        }

        var compressedQueryJson = await CompressAsync(queryJson);

        return new SerializableQuery(
            queryType.AssemblyQualifiedName ?? queryType.FullName ?? queryType.Name,
            compressedQueryJson,
            queryAssemblyVersion);
    }

    public async Task<ResultBox<IQueryCommon>> ToQueryAsync(SekibanDomainTypes domainTypes)
    {
        try
        {
            Type? queryType = null;
            try
            {
                // Use GetPayloadTypeByName from QueryTypes for better type resolution
                queryType = domainTypes.QueryTypes.GetPayloadTypeByName(QueryTypeName);

                if (queryType == null)
                {
                    return ResultBox<IQueryCommon>.FromException(
                        new InvalidOperationException($"Query type not found: {QueryTypeName}"));
                }
            }
            catch (Exception ex)
            {
                return ResultBox<IQueryCommon>.FromException(
                    new InvalidOperationException($"Failed to get query type: {QueryTypeName}", ex));
            }

            if (CompressedQueryJson.Length > 0)
            {
                var decompressedJson = await DecompressAsync(CompressedQueryJson);
                IQueryCommon? query;
                try
                {
                    query = JsonSerializer.Deserialize(
                        decompressedJson,
                        queryType,
                        domainTypes.JsonSerializerOptions) as IQueryCommon;
                }
                catch (NotSupportedException)
                {
                    // Fallback to default options
                    query = JsonSerializer.Deserialize(decompressedJson, queryType) as IQueryCommon;
                }

                if (query == null)
                {
                    return ResultBox<IQueryCommon>.FromException(
                        new InvalidOperationException($"Failed to deserialize query: {QueryTypeName}"));
                }

                return ResultBox<IQueryCommon>.FromValue(query);
            }

            return ResultBox<IQueryCommon>.FromException(new InvalidOperationException("No query data to deserialize"));
        }
        catch (Exception ex)
        {
            return ResultBox<IQueryCommon>.FromException(
                new InvalidOperationException($"Failed to convert to query: {QueryTypeName}", ex));
        }
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
