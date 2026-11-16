using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Orleans.CodeGeneration;
using Sekiban.Dcb;
using Sekiban.Dcb.Queries;

namespace Sekiban.Dcb.Orleans.Serialization;

[GenerateSerializer]
public sealed record SerializableListQueryResult
{
    [Id(0)]
    public int? TotalCount { get; init; }
    [Id(1)]
    public int? TotalPages { get; init; }
    [Id(2)]
    public int? CurrentPage { get; init; }
    [Id(3)]
    public int? PageSize { get; init; }

    [Id(4)]
    public string RecordTypeName { get; init; } = string.Empty;

    [Id(5)]
    public string QueryTypeName { get; init; } = string.Empty;

    [Id(6)]
    public byte[] CompressedItemsJson { get; init; } = Array.Empty<byte>();

    [Id(7)]
    public byte[] CompressedQueryJson { get; init; } = Array.Empty<byte>();

    [Id(8)]
    public string ItemsAssemblyVersion { get; init; } = string.Empty;

    /// <summary>
    ///     Indicates whether the projection is still catching up from the event store.
    ///     When true, the results may be incomplete or stale.
    /// </summary>
    [Id(9)]
    public bool IsCatchUpInProgress { get; init; }

    public SerializableListQueryResult() { }

    private SerializableListQueryResult(
        int? totalCount,
        int? totalPages,
        int? currentPage,
        int? pageSize,
        string recordTypeName,
        string queryTypeName,
        byte[] compressedItemsJson,
        byte[] compressedQueryJson,
        string itemsAssemblyVersion)
    {
        TotalCount = totalCount;
        TotalPages = totalPages;
        CurrentPage = currentPage;
        PageSize = pageSize;
        RecordTypeName = recordTypeName;
        QueryTypeName = queryTypeName;
        CompressedItemsJson = compressedItemsJson;
        CompressedQueryJson = compressedQueryJson;
        ItemsAssemblyVersion = itemsAssemblyVersion;
    }

    public static async Task<SerializableListQueryResult> CreateFromAsync(
        ListQueryResultGeneral result,
        JsonSerializerOptions options)
    {
        var queryType = result.Query.GetType();
        var recordTypeName = result.RecordType;
        var itemsAssemblyVersion = "0.0.0.0";

        var itemsList = result.Items?.ToList() ?? new List<object>();
        if (itemsList.Count > 0)
        {
            var firstItemType = itemsList[0].GetType();
            recordTypeName = firstItemType.AssemblyQualifiedName ?? firstItemType.FullName ?? recordTypeName;
            itemsAssemblyVersion = firstItemType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        }

        byte[] itemsJson;
        try
        {
            itemsJson = JsonSerializer.SerializeToUtf8Bytes(itemsList, options);
        }
        catch (NotSupportedException)
        {
            itemsJson = JsonSerializer.SerializeToUtf8Bytes(itemsList);
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

        var compressedItemsJson = await CompressAsync(itemsJson);
        var compressedQueryJson = await CompressAsync(queryJson);

        return new SerializableListQueryResult
        {
            TotalCount = result.TotalCount,
            TotalPages = result.TotalPages,
            CurrentPage = result.CurrentPage,
            PageSize = result.PageSize,
            RecordTypeName = recordTypeName,
            QueryTypeName = queryType.AssemblyQualifiedName ?? queryType.FullName ?? queryType.Name,
            CompressedItemsJson = compressedItemsJson,
            CompressedQueryJson = compressedQueryJson,
            ItemsAssemblyVersion = itemsAssemblyVersion,
            IsCatchUpInProgress = result.IsCatchUpInProgress
        };
    }

    /// <summary>Serializes a list query result together with the query that produced it.</summary>
    public static async Task<SerializableListQueryResult> CreateFromResultBoxAsync(
        IListQueryResult listQueryResult,
        IListQueryCommon originalQuery,
        JsonSerializerOptions options)
    {
        var resultType = listQueryResult.GetType();

        var itemsProperty = resultType.GetProperty("Items");
        var itemsEnumerable = itemsProperty?.GetValue(listQueryResult) as IEnumerable ?? Array.Empty<object>();
        var items = itemsEnumerable.Cast<object>().ToList();
        var recordTypeName = items.Count > 0
            ? items[0].GetType().AssemblyQualifiedName ?? items[0].GetType().FullName ?? items[0].GetType().Name
            : resultType.IsGenericType
                ? resultType.GetGenericArguments().LastOrDefault()?.AssemblyQualifiedName ??
                  resultType.GetGenericArguments().LastOrDefault()?.FullName ??
                  string.Empty
                : string.Empty;

        var listResultGeneral = new ListQueryResultGeneral(
            listQueryResult.TotalCount,
            listQueryResult.TotalPages,
            listQueryResult.CurrentPage,
            listQueryResult.PageSize,
            items,
            recordTypeName,
            originalQuery,
            listQueryResult.IsCatchUpInProgress);

        return await CreateFromAsync(listResultGeneral, options);
    }

    /// <summary>
    ///     Deserializes the list query result. An unknown query or record type and a query that does not deserialize
    ///     fault the returned task with that failure; any other deserialization failure is reported as an
    ///     <see cref="InvalidOperationException" /> wrapping it.
    /// </summary>
    public async Task<ListQueryResultGeneral> ToListQueryResultAsync(DcbDomainTypes domainTypes)
    {
        (ListQueryResultGeneral? Result, Exception? Failure) outcome;
        try
        {
            outcome = await ReadListQueryResultAsync(domainTypes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to deserialize list query result: {RecordTypeName}", ex);
        }

        return outcome.Failure is null ? outcome.Result! : throw outcome.Failure;
    }

    private async Task<(ListQueryResultGeneral? Result, Exception? Failure)> ReadListQueryResultAsync(
        DcbDomainTypes domainTypes)
    {
        var queryType = SerializableQueryParameter.TryResolveType(QueryTypeName, domainTypes, out var queryTypeFailure);
        if (queryType is null)
        {
            return (null, queryTypeFailure);
        }

        IListQueryCommon? query = null;
        if (CompressedQueryJson.Length > 0)
        {
            var decompressedQueryJson = await DecompressAsync(CompressedQueryJson);
            try
            {
                query = JsonSerializer.Deserialize(
                    decompressedQueryJson,
                    queryType,
                    domainTypes.JsonSerializerOptions) as IListQueryCommon;
            }
            catch (NotSupportedException)
            {
                query = JsonSerializer.Deserialize(decompressedQueryJson, queryType) as IListQueryCommon;
            }
        }

        if (query == null)
        {
            return (null, new InvalidOperationException($"Failed to deserialize query: {QueryTypeName}"));
        }

        IEnumerable<object> items = Array.Empty<object>();
        if (CompressedItemsJson.Length > 0 && !string.IsNullOrWhiteSpace(RecordTypeName))
        {
            var recordType = SerializableQueryParameter.TryResolveType(RecordTypeName, domainTypes, out var recordTypeFailure);
            if (recordType is null)
            {
                return (null, recordTypeFailure);
            }

            var listType = typeof(List<>).MakeGenericType(recordType);
            var decompressedItemsJson = await DecompressAsync(CompressedItemsJson);
            try
            {
                var deserialized = JsonSerializer.Deserialize(
                    decompressedItemsJson,
                    listType,
                    domainTypes.JsonSerializerOptions) as IEnumerable;
                items = deserialized?.Cast<object>().ToList() ?? new List<object>();
            }
            catch (NotSupportedException)
            {
                var deserialized = JsonSerializer.Deserialize(decompressedItemsJson, listType) as IEnumerable;
                items = deserialized?.Cast<object>().ToList() ?? new List<object>();
            }
        }

        return (new ListQueryResultGeneral(
            TotalCount,
            TotalPages,
            CurrentPage,
            PageSize,
            items,
            RecordTypeName,
            query,
            IsCatchUpInProgress), null);
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
