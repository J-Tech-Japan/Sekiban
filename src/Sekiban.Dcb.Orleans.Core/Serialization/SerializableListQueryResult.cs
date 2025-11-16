using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Orleans.CodeGeneration;
using ResultBoxes;
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

        return new SerializableListQueryResult(
            result.TotalCount,
            result.TotalPages,
            result.CurrentPage,
            result.PageSize,
            recordTypeName,
            queryType.AssemblyQualifiedName ?? queryType.FullName ?? queryType.Name,
            compressedItemsJson,
            compressedQueryJson,
            itemsAssemblyVersion);
    }

    public static async Task<ResultBox<SerializableListQueryResult>> CreateFromResultBoxAsync(
        ResultBox<IListQueryResult> resultBox,
        IListQueryCommon originalQuery,
        JsonSerializerOptions options)
    {
        if (!resultBox.IsSuccess)
        {
            return ResultBox<SerializableListQueryResult>.FromException(resultBox.GetException());
        }

        var listQueryResult = resultBox.GetValue();
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
            originalQuery);

        var serializable = await CreateFromAsync(listResultGeneral, options);

        return ResultBox<SerializableListQueryResult>.FromValue(serializable);
    }

    public async Task<ResultBox<ListQueryResultGeneral>> ToListQueryResultAsync(DcbDomainTypes domainTypes)
    {
        try
        {
            var queryTypeResult = SerializableQueryParameter.ResolveType(QueryTypeName, domainTypes);
            if (!queryTypeResult.IsSuccess)
            {
                return ResultBox<ListQueryResultGeneral>.FromException(queryTypeResult.GetException());
            }

            var queryType = queryTypeResult.GetValue();

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
                return ResultBox<ListQueryResultGeneral>.FromException(
                    new InvalidOperationException($"Failed to deserialize query: {QueryTypeName}"));
            }

            IEnumerable<object> items = Array.Empty<object>();
            if (CompressedItemsJson.Length > 0 && !string.IsNullOrWhiteSpace(RecordTypeName))
            {
                var recordTypeResult = SerializableQueryParameter.ResolveType(RecordTypeName, domainTypes);
                if (!recordTypeResult.IsSuccess)
                {
                    return ResultBox<ListQueryResultGeneral>.FromException(recordTypeResult.GetException());
                }

                var recordType = recordTypeResult.GetValue();
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

            var listQueryResult = new ListQueryResultGeneral(
                TotalCount,
                TotalPages,
                CurrentPage,
                PageSize,
                items,
                RecordTypeName,
                query);

            return ResultBox<ListQueryResultGeneral>.FromValue(listQueryResult);
        }
        catch (Exception ex)
        {
            return ResultBox<ListQueryResultGeneral>.FromException(
                new InvalidOperationException($"Failed to deserialize list query result: {RecordTypeName}", ex));
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
