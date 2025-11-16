using System;
using System.Threading.Tasks;
using System.IO;
using Orleans.CodeGeneration;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Queries;
using System.IO.Compression;
using System.Text.Json;

namespace Sekiban.Dcb.Orleans.Serialization;

[GenerateSerializer]
public sealed record SerializableQueryParameter
{
    [Id(0)]
    public string QueryTypeName { get; init; } = string.Empty;

    [Id(1)]
    public byte[] CompressedQueryJson { get; init; } = Array.Empty<byte>();

    [Id(2)]
    public string QueryAssemblyVersion { get; init; } = string.Empty;

    public SerializableQueryParameter() { }

    private SerializableQueryParameter(string queryTypeName, byte[] compressedQueryJson, string queryAssemblyVersion)
    {
        QueryTypeName = queryTypeName;
        CompressedQueryJson = compressedQueryJson;
        QueryAssemblyVersion = queryAssemblyVersion;
    }

    public static async Task<SerializableQueryParameter> CreateFromAsync(
        object query,
        JsonSerializerOptions options)
    {
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        var queryType = query.GetType();
        var queryAssemblyVersion = queryType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        byte[] queryJson;
        try
        {
            queryJson = JsonSerializer.SerializeToUtf8Bytes(query, queryType, options);
        }
        catch (NotSupportedException)
        {
            queryJson = JsonSerializer.SerializeToUtf8Bytes(query, queryType);
        }

        var compressedQueryJson = await CompressAsync(queryJson);

        return new SerializableQueryParameter(
            queryType.AssemblyQualifiedName ?? queryType.FullName ?? queryType.Name,
            compressedQueryJson,
            queryAssemblyVersion);
    }

    public async Task<ResultBox<object>> ToQueryAsync(DcbDomainTypes domainTypes)
    {
        try
        {
            var queryTypeResult = ResolveType(QueryTypeName, domainTypes);
            if (!queryTypeResult.IsSuccess)
            {
                return ResultBox<object>.FromException(queryTypeResult.GetException());
            }

            var queryType = queryTypeResult.GetValue();

            if (CompressedQueryJson.Length == 0)
            {
                return ResultBox<object>.FromException(
                    new InvalidOperationException("No query payload to deserialize."));
            }

            var decompressedJson = await DecompressAsync(CompressedQueryJson);

            object? query;
            try
            {
                query = JsonSerializer.Deserialize(
                    decompressedJson,
                    queryType,
                    domainTypes.JsonSerializerOptions);
            }
            catch (NotSupportedException)
            {
                query = JsonSerializer.Deserialize(decompressedJson, queryType);
            }

            if (query == null)
            {
                return ResultBox<object>.FromException(
                    new InvalidOperationException($"Failed to deserialize query: {QueryTypeName}"));
            }

            return ResultBox<object>.FromValue(query);
        }
        catch (Exception ex)
        {
            return ResultBox<object>.FromException(ex);
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

    internal static ResultBox<Type> ResolveType(string typeName, DcbDomainTypes domainTypes)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return ResultBox<Type>.FromException(new InvalidOperationException("Query type name is empty."));
        }

        try
        {
            var type = domainTypes.QueryTypes.GetTypeByName(typeName);
            if (type != null)
            {
                return ResultBox<Type>.FromValue(type);
            }
        }
        catch (Exception ex)
        {
            return ResultBox<Type>.FromException(
                new InvalidOperationException($"Failed to resolve query type from domain types: {typeName}", ex));
        }

        var resolved = Type.GetType(typeName, throwOnError: false);
        if (resolved != null)
        {
            return ResultBox<Type>.FromValue(resolved);
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var candidate = assembly.GetType(typeName);
            if (candidate != null)
            {
                return ResultBox<Type>.FromValue(candidate);
            }
        }

        return ResultBox<Type>.FromException(
            new InvalidOperationException($"Query type not found: {typeName}"));
    }
}
