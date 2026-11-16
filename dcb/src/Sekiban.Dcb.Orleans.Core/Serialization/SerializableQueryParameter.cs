using System;
using System.Threading.Tasks;
using System.IO;
using Orleans.CodeGeneration;
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

    /// <summary>
    ///     Deserializes the query. An unknown query type, an empty payload or a payload that does not deserialize faults
    ///     the returned task.
    /// </summary>
    public async Task<object> ToQueryAsync(DcbDomainTypes domainTypes)
    {
        var queryType = TryResolveType(QueryTypeName, domainTypes, out var typeFailure) ?? throw typeFailure!;

        if (CompressedQueryJson.Length == 0)
        {
            throw new InvalidOperationException("No query payload to deserialize.");
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

        return query ?? throw new InvalidOperationException($"Failed to deserialize query: {QueryTypeName}");
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

    /// <summary>
    ///     Resolves a query, result or record type by name. A name that cannot be resolved returns <c>null</c> with the
    ///     failure to report in <paramref name="failure" />, so the deserializers can report it unwrapped.
    /// </summary>
    internal static Type? TryResolveType(string typeName, DcbDomainTypes domainTypes, out Exception? failure)
    {
        failure = null;
        if (string.IsNullOrWhiteSpace(typeName))
        {
            failure = new InvalidOperationException("Query type name is empty.");
            return null;
        }

        try
        {
            var type = domainTypes.QueryTypes.GetTypeByName(typeName);
            if (type != null)
            {
                return type;
            }
        }
        catch (Exception ex)
        {
            failure = new InvalidOperationException($"Failed to resolve query type from domain types: {typeName}", ex);
            return null;
        }

        var resolved = Type.GetType(typeName, throwOnError: false);
        if (resolved != null)
        {
            return resolved;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var candidate = assembly.GetType(typeName);
            if (candidate != null)
            {
                return candidate;
            }
        }

        failure = new InvalidOperationException($"Query type not found: {typeName}");
        return null;
    }
}
